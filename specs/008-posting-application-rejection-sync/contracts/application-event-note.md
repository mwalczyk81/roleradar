# Contract: `note` application event type

## Shared list

`contracts/application-enums.json` `eventTypes` becomes:

```text
applied, follow_up, interview, offer, offer_accepted, offer_declined, rejected, withdrawn, note
```

Three things move together in one phase: the JSON snapshot, the .NET drift test (`ApplicationEnumsContractSnapshotTests`) and the web equality test (`web/tests/unit/application-enums.test.ts`). `web/app/lib/application-enums.ts` takes its values from the snapshot's list; metadata adds `note` with label "Note".

## Semantics

| Aspect | Rule |
| --- | --- |
| Outcome | Unchanged by a note |
| Posting disposition | Unchanged by a note |
| Response, interview, offer, rejection milestones | A note never sets any of them |
| `last_event_at`, `has_last_event` | A note is ignored |
| Insights counts, rates, ghost rule | Identical with and without notes |
| Append | Allowed through the existing append endpoint and `application_record` event input; notes text is the event `notes` field |

## Evidence required

1. Unit: `ComputeApplicationMilestones` over the same events with and without notes returns equal milestones.
2. Insights oracle and seed: add note events to fictional applications (including one old, no-response application that must stay ghosted); the oracle's expected figures do not change, and the API parity tests still pass.
3. Web unit: the equality test fails if the list drifts from the snapshot.
