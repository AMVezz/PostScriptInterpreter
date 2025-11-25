using System.IO;
using Xunit;
using PostScriptInterpreter;

public class InterpreterTests
{
    private static string Run(string code, bool lexical = false)
    {
        var sw = new StringWriter();
        var interp = new Interpreter(lexical, sw);
        interp.Run(code);
        return sw.ToString();
    }

    [Fact]
    public void Arithmetic_Works()
    {
        var outp = Run("3 4 add =");
        Assert.Contains("7", outp);
    }

    [Fact]
    public void Dict_Def_And_Lookup()
    {
        var outp = Run("/x 10 def x 2 mul =");
        Assert.Contains("20", outp);
    }

    [Fact]
    public void Ifelse_Works()
    {
        var outp = Run("true { 1 } { 2 } ifelse =");
        Assert.Contains("1", outp);
    }

    [Fact]
    public void For_Loop_Works()
    {
        var outp = Run("0 1 3 { dup } for count =");
        Assert.Contains("4", outp); // 0 1 2 3
    }

    [Fact]
    public void DynamicVsLexical_Differs()
    {
        string code = @"
            /x 10 def
            /f { x } def
            /g {
                /x 99 def
                f
            } def
            g =
        ";

        var dyn = Run(code, lexical: false);
        var lex = Run(code, lexical: true);

        Assert.Contains("99", dyn); // dynamic sees inner x
        Assert.Contains("10", lex); // lexical captures outer x
    }
}
