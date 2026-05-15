---
title: Delegation
sdk: csharp
spec_sections: ["§10"]
order: 10
kind: reference
---

An agent delegates work by submitting a child job and emitting a `delegate` event on its own stream linking the two. The child's lease MUST be a subset of the parent's (spec §9.4).

```csharp
server.RegisterAgent("parent", async (ctx, ct) =>
{
    var child = await childClient.SubmitAsync(
        agent: "research",
        leaseRequest: new Lease(new Dictionary<string, IReadOnlyList<string>>
        {
            ["net.fetch"] = new[] { "https://*.example.com/*" },
            ["cost.budget"] = new[] { "USD:0.50" },
        }));
    await ctx.DelegateAsync(child.JobId.Value, "research", new { topic = "arcp" }, ct);
    var summary = await child.Result;
    return new { summary };
});
```

`JobContext.DelegateAsync(childJobId, agent, input)` emits a `delegate` event on the parent's stream so observers know about the link.

Cancellation: cancelling the parent does not propagate to children — children are independent jobs with their own lease and submitter. The application coordinates cancellation if needed.

Trace propagation (spec §11): child jobs SHOULD reuse the parent's `trace_id` so the spans link in any backend. Set `TraceId` explicitly on the child submission when needed.
