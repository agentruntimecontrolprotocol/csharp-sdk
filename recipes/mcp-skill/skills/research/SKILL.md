# research

An ARCP-backed research skill.  Submit a topic and receive a synthesised
summary drawn from the knowledge base.

## Tool

```
name: research
description: Research a topic and return a structured summary of findings.
input_schema:
  type: object
  properties:
    topic:
      type: string
      description: The subject to research (e.g. "transformer architecture").
  required: [topic]
```

## Usage

Invoke from Claude Code or any MCP client:

```
/research topic="retrieval-augmented generation"
```

The skill adapter submits an ARCP job to the running `mcp-skill` server,
streams progress log events back to the caller, and returns the final summary
when the job completes.

## Running the server

```sh
dotnet run --project recipes/mcp-skill
```

Or install the CLI tool and serve the compiled assembly:

```sh
dotnet tool install --global Arcp.Cli
arcp serve --assembly mcp-skill.dll --address http://127.0.0.1:7777/arcp --token tok-dev
```

## Related

- [mcp-skill recipe](../../README.md)
- [Arcp.Cli reference](../../../../docs/projects/Arcp.Cli.md)
