using ARCP;

// Phase 7 will replace this with full System.CommandLine wiring (`arcp serve`,
// `arcp tail`, `arcp send`, `arcp replay`). For Phase 0, the binary just
// prints the SDK version so `dotnet build` and `dotnet pack` produce a
// working tool entrypoint.
Console.WriteLine($"arcp v{ProtocolVersion.Sdk} (protocol {ProtocolVersion.Wire})");
return 0;
