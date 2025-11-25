
namespace PostScriptInterpreter
{
    public enum PSKind
    {
        Int, Real, Bool, String, Name, LiteralName, Array, Proc, Dict, Mark, Null
    }

    public abstract class PSValue
    {
        public abstract PSKind Kind { get; }
        public virtual double AsNumber() => throw new InvalidOperationException($"Not a number: {Kind}");
        public virtual bool AsBool() => throw new InvalidOperationException($"Not a bool: {Kind}");
        public virtual string AsString() => throw new InvalidOperationException($"Not a string: {Kind}");
        public virtual List<PSValue> AsArray() => throw new InvalidOperationException($"Not an array: {Kind}");
        public virtual Dictionary<string, PSValue> AsDict() => throw new InvalidOperationException($"Not a dict: {Kind}");
        public override string ToString() => Kind.ToString();
    }

    public sealed class PSBuiltin : PSValue
    {
        private readonly Action action;

        public PSBuiltin(Action action)
        {
            this.action = action;
        }

        public override PSKind Kind => PSKind.Proc;  // treated as executable

        public void Invoke() => action();

        public override string ToString() => "<builtin>";
    }

    public sealed class PSInt : PSValue
    {
        public int Value { get; }
        public PSInt(int v) { Value = v; }
        public override PSKind Kind => PSKind.Int;
        public override double AsNumber() => Value;
        public override string ToString() => Value.ToString();
    }

    public sealed class PSReal : PSValue
    {
        public double Value { get; }
        public PSReal(double v) { Value = v; }
        public override PSKind Kind => PSKind.Real;
        public override double AsNumber() => Value;
        public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public sealed class PSBool : PSValue
    {
        public bool Value { get; }
        public PSBool(bool v) { Value = v; }
        public override PSKind Kind => PSKind.Bool;
        public override bool AsBool() => Value;
        public override string ToString() => Value ? "true" : "false";
    }

    // Stored without parentheses.
    public sealed class PSString : PSValue
    {
        public string Value { get; }
        public PSString(string v) { Value = v; }
        public override PSKind Kind => PSKind.String;
        public override string AsString() => Value;
        public override string ToString() => $"({Value})";
    }

    // Executable name (looked up)
    public sealed class PSName : PSValue
    {
        public string Value { get; }
        public PSName(string v) { Value = v; }
        public override PSKind Kind => PSKind.Name;
        public override string AsString() => Value;
        public override string ToString() => Value;
    }

    // Literal name (/x)
    public sealed class PSLiteralName : PSValue
    {
        public string Value { get; }
        public PSLiteralName(string v) { Value = v; }
        public override PSKind Kind => PSKind.LiteralName;
        public override string AsString() => Value;
        public override string ToString() => "/" + Value;
    }

    public sealed class PSArray : PSValue
    {
        public List<PSValue> Elements { get; }
        public PSArray(List<PSValue> el) { Elements = el; }
        public override PSKind Kind => PSKind.Array;
        public override List<PSValue> AsArray() => Elements;
        public override string ToString() => $"[{string.Join(" ", Elements)}]";
    }

    // Procedure (code array). For lexical scoping, we store static env.
    public sealed class PSProc : PSValue
    {
        public List<PSValue> Code { get; }
        public EnvFrame? StaticEnv { get; } // null => dynamic or top-level
        public PSProc(List<PSValue> code, EnvFrame? staticEnv)
        {
            Code = code;
            StaticEnv = staticEnv;
        }
        public override PSKind Kind => PSKind.Proc;
        public override string ToString() => "{ " + string.Join(" ", Code) + " }";
    }

    public sealed class PSDict : PSValue
    {
        public Dictionary<string, PSValue> Dict { get; }
        public PSDict(Dictionary<string, PSValue> d) { Dict = d; }
        public override PSKind Kind => PSKind.Dict;
        public override Dictionary<string, PSValue> AsDict() => Dict;
        public override string ToString() => $"<<dict {Dict.Count}>>";
    }

    public sealed class PSMark : PSValue
    {
        public static readonly PSMark Instance = new PSMark();
        private PSMark() { }
        public override PSKind Kind => PSKind.Mark;
        public override string ToString() => "-mark-";
    }

    public sealed class PSNull : PSValue
    {
        public static readonly PSNull Instance = new PSNull();
        private PSNull() { }
        public override PSKind Kind => PSKind.Null;
        public override string ToString() => "null";
    }

    // Environment frame for lexical captured chain
    public sealed class EnvFrame
    {
        public Dictionary<string, PSValue> Dict { get; }
        public EnvFrame? Parent { get; }
        public EnvFrame(Dictionary<string, PSValue> dict, EnvFrame? parent)
        {
            Dict = dict;
            Parent = parent;
        }

        public PSValue? Lookup(string name)
        {
            if (Dict.TryGetValue(name, out var v)) return v;
            return Parent?.Lookup(name);
        }
    }
}
