using System.Globalization;

namespace PostScriptInterpreter
{
    public sealed class Interpreter
    {
        private readonly bool lexicalScoping;
        private readonly TextWriter output;

        private readonly Stack<PSValue> opStack = new();
        private readonly Stack<Dictionary<string, PSValue>> dictStack = new();
        private bool quitFlag = false;

        public Interpreter(bool lexicalScoping = false, TextWriter? output = null)
        {
            this.lexicalScoping = lexicalScoping;
            this.output = output ?? Console.Out;

            // bottom/system dictionary
            dictStack.Push(new Dictionary<string, PSValue>());
            InstallBuiltins(dictStack.Peek());
        }

        public void Run(string program)
        {
            var tokens = Tokenize(program);
            var code = Parse(tokens);
            Exec(code, staticEnv: null);
        }

        // -------------------- TOKENIZER --------------------
        private static List<string> Tokenize(string s)
        {
            var toks = new List<string>();
            int i = 0;

            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                // comment to end of line
                if (c == '%')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }

                if ("[]{}()".Contains(c))
                {
                    if (c == '(')
                    {
                        i++;
                        string str = "";
                        int depth = 1;
                        while (i < s.Length && depth > 0)
                        {
                            if (s[i] == '\\' && i + 1 < s.Length)
                            {
                                str += s[i + 1];
                                i += 2;
                            }
                            else if (s[i] == '(') { depth++; str += '('; i++; }
                            else if (s[i] == ')')
                            {
                                depth--;
                                if (depth > 0) str += ')';
                                i++;
                            }
                            else
                            {
                                str += s[i];
                                i++;
                            }
                        }
                        toks.Add("(" + str + ")");
                    }
                    else
                    {
                        toks.Add(c.ToString());
                        i++;
                    }
                    continue;
                }

                int j = i;
                while (j < s.Length &&
                       !char.IsWhiteSpace(s[j]) &&
                       !"[]{}()".Contains(s[j]) &&
                       s[j] != '%')
                {
                    j++;
                }

                toks.Add(s.Substring(i, j - i));
                i = j;
            }

            return toks;
        }

        // -------------------- PARSER --------------------
        private List<PSValue> Parse(List<string> tokens)
        {
            int pos = 0;
            return ParseUntil(tokens, ref pos, null);
        }

        private List<PSValue> ParseUntil(List<string> tokens, ref int pos, string? end)
        {
            var items = new List<PSValue>();

            while (pos < tokens.Count)
            {
                string t = tokens[pos++];

                if (end != null && t == end)
                    break;

                if (t == "{")
                {
                    var block = ParseUntil(tokens, ref pos, "}");
                    // Do NOT capture lexical env here.
                    // Capture happens at runtime when this proc literal is encountered.
                    items.Add(new PSProc(block, staticEnv: null));
                    continue;
                }

                if (t == "[")
                {
                    var arr = ParseUntil(tokens, ref pos, "]");
                    items.Add(new PSArray(arr));
                    continue;
                }

                items.Add(ParseAtom(t));
            }

            return items;
        }

        private static PSValue ParseAtom(string t)
        {
            if (t.StartsWith("(") && t.EndsWith(")"))
                return new PSString(t[1..^1]);

            if (t.StartsWith("/"))
                return new PSLiteralName(t.Substring(1));

            if (t == "true" || t == "false")
                return new PSBool(t == "true");

            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
                return new PSInt(iv);

            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double dv))
                return new PSReal(dv);

            return new PSName(t);
        }

        // Snapshot current dict stack into a lexical environment chain (shallow clone of dict entries).
        private EnvFrame CurrentLexicalEnvSnapshot()
        {
            EnvFrame? env = null;

            // dictStack enumerates top -> bottom; we need bottom -> top
            foreach (var d in dictStack.Reverse())
            {
                var cloned = new Dictionary<string, PSValue>(d); // shallow copy ok
                env = new EnvFrame(cloned, env);
            }

            return env!;
        }

        // -------------------- EXECUTION CORE --------------------
        private void Exec(List<PSValue> code, EnvFrame? staticEnv)
        {
            int ip = 0;
            while (ip < code.Count && !quitFlag)
            {
                Exec(code[ip++], staticEnv);
            }
        }

        private void Exec(PSValue v, EnvFrame? staticEnv)
        {
            if (v is PSBuiltin b)
            {
                b.Invoke();
                return;
            }
            Step(v, staticEnv);
        }

        private void Step(PSValue v, EnvFrame? staticEnv)
        {
            switch (v.Kind)
            {
                case PSKind.Int:
                case PSKind.Real:
                case PSKind.Bool:
                case PSKind.String:
                case PSKind.Array:
                case PSKind.Dict:
                case PSKind.LiteralName:
                    opStack.Push(v);
                    return;

                case PSKind.Proc:
                    // Runtime lexical capture: when a proc literal is encountered.
                    if (lexicalScoping && v is PSProc procLit && procLit.StaticEnv == null)
                    {
                        opStack.Push(new PSProc(procLit.Code, CurrentLexicalEnvSnapshot()));
                    }
                    else
                    {
                        opStack.Push(v);
                    }
                    return;

                case PSKind.Name:
                {
                    string name = ((PSName)v).Value;
                    var resolved = Lookup(name, staticEnv);

                    if (resolved == null)
                        throw new Exception($"Undefined name: {name}");

                    if (resolved is PSBuiltin builtin)
                    {
                        builtin.Invoke();
                    }
                    else if (resolved is PSProc proc)
                    {
                        var envToUse = lexicalScoping ? proc.StaticEnv : CurrentLexicalEnvSnapshot();
                        Exec(proc.Code, envToUse);
                    }
                    else
                    {
                        opStack.Push(resolved);
                    }
                    return;
                }

                default:
                    throw new Exception($"Cannot execute: {v}");
            }
        }

        private PSValue? Lookup(string name, EnvFrame? staticEnv)
        {
            if (lexicalScoping)
            {
                // 1) lexical/static first
                if (staticEnv != null)
                {
                    var v = staticEnv.Lookup(name);
                    if (v != null) return v;
                }

                // 2) system dictionary only (builtins)
                var sys = dictStack.Last();
                if (sys.TryGetValue(name, out var sysVal))
                    return sysVal;

                return null;
            }
            else
            {
                // dynamic: search dict stack top -> bottom
                foreach (var d in dictStack)
                    if (d.TryGetValue(name, out var v))
                        return v;

                return null;
            }
        }

        private void Define(string name, PSValue value)
        {
            dictStack.Peek()[name] = value;
        }

        // -------------------- STACK HELPERS --------------------
        private PSValue Pop() => opStack.Pop();
        private PSValue Peek() => opStack.Peek();

        private PSValue PopExpect(params PSKind[] kinds)
        {
            var v = Pop();
            if (!kinds.Contains(v.Kind))
                throw new Exception($"Type error: expected {string.Join("/", kinds)}, got {v.Kind}");
            return v;
        }

        private int PopInt() => (int)PopExpect(PSKind.Int).AsNumber();
        private double PopNumber() => PopExpect(PSKind.Int, PSKind.Real).AsNumber();
        private bool PopBool() => PopExpect(PSKind.Bool).AsBool();
        private PSProc PopProc() => (PSProc)PopExpect(PSKind.Proc);

        private Dictionary<string, PSValue> PopDict()
            => PopExpect(PSKind.Dict).AsDict();

        private void PushNumber(double x)
        {
            if (Math.Abs(x - Math.Round(x)) < 1e-12)
                opStack.Push(new PSInt((int)Math.Round(x)));
            else
                opStack.Push(new PSReal(x));
        }

        // -------------------- BUILTINS --------------------
        private void InstallBuiltins(Dictionary<string, PSValue> sys)
        {
            void B(string n, Action a) => sys[n] = new PSBuiltin(a);

            // stack
            B("pop", () => Pop());
            B("exch", () =>
            {
                var a = Pop(); var b = Pop();
                opStack.Push(a); opStack.Push(b);
            });
            B("dup", () => opStack.Push(Peek()));
            B("clear", () => opStack.Clear());
            B("count", () => opStack.Push(new PSInt(opStack.Count)));
            B("copy", () =>
            {
                int n = PopInt();
                if (n < 0 || n > opStack.Count) throw new Exception("rangecheck");
                var temp = opStack.Reverse().Take(n).Reverse().ToList();
                foreach (var v in temp) opStack.Push(v);
            });

            // arithmetic
            B("add", () => PushNumber(PopNumber() + PopNumber()));
            B("sub", () =>
            {
                var b = PopNumber();
                var a = PopNumber();
                PushNumber(a - b);
            });
            B("mul", () => PushNumber(PopNumber() * PopNumber()));
            B("div", () =>
            {
                var b = PopNumber(); var a = PopNumber();
                PushNumber(a / b);
            });
            B("mod", () =>
            {
                int b = PopInt(); int a = PopInt();
                opStack.Push(new PSInt(a % b));
            });

            // comparison / boolean
            B("eq", () =>
            {
                var b = Pop(); var a = Pop();
                opStack.Push(new PSBool(EqualsPS(a, b)));
            });
            B("ne", () =>
            {
                var b = Pop(); var a = Pop();
                opStack.Push(new PSBool(!EqualsPS(a, b)));
            });
            B("gt", () =>
            {
                var b = PopNumber();
                var a = PopNumber();
                opStack.Push(new PSBool(a > b));
            });
            B("lt", () =>
            {
                var b = PopNumber();
                var a = PopNumber();
                opStack.Push(new PSBool(a < b));
            });

            // dict operations
            B("dict", () =>
            {
                PopInt(); // size ignored
                opStack.Push(new PSDict(new Dictionary<string, PSValue>()));
            });
            B("begin", () => dictStack.Push(PopDict()));
            B("end", () =>
            {
                if (dictStack.Count <= 1) throw new Exception("dictstack underflow");
                dictStack.Pop();
            });
            B("def", () =>
            {
                var val = Pop();
                var key = PopExpect(PSKind.LiteralName).AsString();
                Define(key, val);
            });

            // flow control
            B("if", () =>
            {
                var proc = PopProc();
                bool cond = PopBool();
                if (cond)
                    Exec(proc.Code, lexicalScoping ? proc.StaticEnv : null);
            });
            B("ifelse", () =>
            {
                var procFalse = PopProc();
                var procTrue = PopProc();
                bool cond = PopBool();
                var chosen = cond ? procTrue : procFalse;
                Exec(chosen.Code, lexicalScoping ? chosen.StaticEnv : null);
            });
            B("repeat", () =>
            {
                var proc = PopProc();
                int n = PopInt();
                for (int i = 0; i < n && !quitFlag; i++)
                    Exec(proc.Code, lexicalScoping ? proc.StaticEnv : null);
            });
            B("for", () =>
            {
                var body = PopProc();
                double limit = PopNumber();
                double inc = PopNumber();
                double init = PopNumber();

                if (inc == 0) throw new Exception("Invalid increment");

                void runLoop(double x)
                {
                    // push index
                    PushNumber(x);

                    // execute body
                    Exec(body.Code, lexicalScoping ? body.StaticEnv : null);

                    // pop unconsumed index
                    if (opStack.Count > 0 &&
                        (opStack.Peek() is PSInt || opStack.Peek() is PSReal) &&
                        Math.Abs(opStack.Peek().AsNumber() - x) < 1e-12)
                    {
                        Pop();
                    }
                }

                if (inc > 0)
                {
                    for (double x = init; x <= limit && !quitFlag; x += inc)
                        runLoop(x);
                }
                else
                {
                    for (double x = init; x >= limit && !quitFlag; x += inc)
                        runLoop(x);
                }
            });

            B("quit", () => quitFlag = true);

            // I/O
            B("print", () => output.Write(PopExpect(PSKind.String).AsString()));
            B("=", () => output.WriteLine(Pop().ToString()));
            B("==", () => output.WriteLine(Pretty(Pop())));
        }

        private static bool EqualsPS(PSValue a, PSValue b)
        {
            if (a.Kind != b.Kind)
            {
                if ((a.Kind == PSKind.Int || a.Kind == PSKind.Real) &&
                    (b.Kind == PSKind.Int || b.Kind == PSKind.Real))
                    return Math.Abs(a.AsNumber() - b.AsNumber()) < 1e-12;
                return false;
            }

            return a.Kind switch
            {
                PSKind.Int or PSKind.Real => Math.Abs(a.AsNumber() - b.AsNumber()) < 1e-12,
                PSKind.Bool => a.AsBool() == b.AsBool(),
                PSKind.String or PSKind.Name or PSKind.LiteralName => a.AsString() == b.AsString(),
                PSKind.Array => a.AsArray().SequenceEqual(b.AsArray()),
                _ => ReferenceEquals(a, b)
            };
        }

        private static string Pretty(PSValue v)
        {
            return v.Kind switch
            {
                PSKind.String => v.ToString(),
                PSKind.Array => "[" + string.Join(" ", v.AsArray().Select(Pretty)) + "]",
                PSKind.Proc => "{ " + string.Join(" ", ((PSProc)v).Code.Select(Pretty)) + " }",
                PSKind.Dict => "<< " + string.Join(" ", v.AsDict().Select(kv => $"/{kv.Key} {Pretty(kv.Value)}")) + " >>",
                _ => v.ToString()
            };
        }
    }
}
