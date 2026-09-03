# SR5 After Run acknowledgement JSON fix

## Scope

This isolated change fixes the exact R5 After Run checkpoint serialization
failure without modifying the API-36 driver, its closed-world schema, timing,
navigation, gate registration, or publication authority.

`Sr5AfterRunReviewAcknowledgements.AllReviewed` is derived from six explicit
review inputs. It now has `[JsonIgnore]`, so the default `JsonSerializer`
persists exactly these fields:

```text
RunContextReviewed
RewardsReviewed
ConsequencesReviewed
ContactsReviewed
GmApprovalReviewed
OwnerApprovalReviewed
```

The managed After Run authority harness now executes five serialization
checks:

1. the JSON object has exactly the six closed-world fields;
2. a normal six-input round trip retains equality and computed completion;
3. the actual durable checkpoint embeds exactly those six fields and survives
   an exact structural/semantic round trip;
4. an injected `"AllReviewed": false` cannot override the derived value and
   is removed on normalization;
5. one false authoritative input remains false after round trip and cannot
   fabricate a complete review.

## Generated authority

`docs/ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json` was refreshed
because it binds the SHA-256 of `Sr5AfterRunWizardModel.cs`. A controlled
temporary regeneration comparison showed that this model hash was the only
generated-field difference.

## Verification

```text
dotnet run --project \
  tests/Chummer.Android.Sr5AfterRunSettlement.Tests/Chummer.Android.Sr5AfterRunSettlement.Tests.csproj \
  -p:ChummerCoreRoot=/docker/chummercomplete-active-worktrees/chummer-core-engine
# 10/10 passed

python3 scripts/materialize_chummer5_editability_inventory.py \
  --chummer5-root /docker/chummer5a \
  --registry /docker/chummercomplete/chummer-design/products/chummer/ANDROID_WINDOWS_FEATURE_PARITY.yaml \
  --presentation-root /docker/chummercomplete-active-worktrees/chummer-presentation \
  --core-root /docker/chummercomplete-active-worktrees/chummer-core-engine \
  --check
# current

python3 -m unittest -v tests/test_chummer5_editability_inventory.py
# 72/72 passed

python3 -m pytest -q \
  tests/test_api36_sr5_after_run_settlement_contract.py \
  tests/test_api36_sr5_after_run_settlement_hosted_contract.py \
  tests/test_sr5_after_run_settlement_source_contract.py
# 29 passed, 32 subtests passed
```

No emulator, device, P0/live branch, E2E timeout, E2E navigation, schema,
push, merge, package seal, upload, or publication state was changed.

The mandatory vexp `run_pipeline` and `verify_done` calls were attempted, but
the local vexp transport returned `Transport closed`; targeted inspection and
the executable suites above were used as the documented fallback.
