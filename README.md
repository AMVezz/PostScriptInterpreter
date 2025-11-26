# PostScript Interpreter

## Build and Run

### Build

Run in the project root:

```
dotnet build
```

### Run Tests

```
dotnet test
```

### Execute Code Manually

You can call the interpreter directly from C#:

```csharp
var interp = new Interpreter(lexicalScoping: false, output: Console.Out);
interp.Run("3 4 add =");
```

## Change Scoping

Scoping mode is selected in the constructor:

```
new Interpreter(false) // dynamic scoping
new Interpreter(true)  // lexical scoping
```

**Dynamic scoping**: variables are resolved by searching the dictionary stack at runtime.

**Lexical scoping**: procedures capture the environment where they were defined, so later redefinitions do not affect them.
