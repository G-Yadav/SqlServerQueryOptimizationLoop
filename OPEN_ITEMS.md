# Open Items

## MCP Server

### Verify database connectivity at startup
The MCP protocol provides no hook between handshake completion and the first tool call. If the server exits before the `initialize` handshake completes, the client (Claude Code, MCP Inspector) only sees a dead pipe — not the actual error. Options to explore:

- **Before `RunAsync()`** — run `SELECT 1` after `Build()`. Fails fast; error visible on stderr but not inside Claude's context.
- **First tool call** — let the first real call surface the error as a returned string. Claude can see and act on it, but startup looks healthy when it may not be.
- **`IHostedService.StartAsync`** — same client-visible behaviour as the `RunAsync()` option; more idiomatic for complex startup sequences.

Decision needed: who needs to see the error — the operator (stderr is enough) or Claude (needs a tool-call-time error string)?

### Lazy loading tool definitions
All tool schemas are sent to the client on `tools/list` during initialization. For 8 tools this is negligible, but worth revisiting if the tool count grows significantly. The protocol supports dynamic tool registration via `notifications/tools/list_changed` — server starts with an empty/minimal list, emits the notification when relevant tools are determined, client re-fetches. Client support is inconsistent in 2026 (Claude Code has open feature requests, most clients don't re-fetch after `list_changed`). Revisit when tool count approaches 50+ and context cost becomes measurable.
