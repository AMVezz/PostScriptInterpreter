namespace PostScriptInterpreter
{
    class Program
    {
        // Usage:
        //   dotnet run --project PostScriptMini -- <file.ps> [--lexical]
        // Or pipe stdin.
        static int Main(string[] args)
        {
            bool lexical = false;
            string? path = null;

            foreach (var a in args)
            {
                if (a == "--lexical" || a == "-l") lexical = true;
                else path = a;
            }

            string input;
            if (path != null)
            {
                input = File.ReadAllText(path);
            }
            else
            {
                input = Console.In.ReadToEnd();
            }

            var interp = new Interpreter(lexical, Console.Out);
            interp.Run(input);
            return 0;
        }
    }
}
