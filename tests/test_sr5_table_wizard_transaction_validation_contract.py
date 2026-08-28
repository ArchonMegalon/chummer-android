from pathlib import Path


REPO = Path(__file__).resolve().parents[1]


def test_transaction_observation_uses_the_public_typed_session_boundary() -> None:
    transaction = (
        REPO / "src/Chummer.Android/Native/Sr5TableWizardTypedTransaction.cs"
    ).read_text(encoding="utf-8")

    assert "new Sr5TableWizardSession().Bind(observed)" in transaction
    assert "Sr5TableWizardProjector.ValidateSnapshot" not in transaction

    for forbidden in (
        "GetMethod(",
        "GetProperty(",
        "System.Reflection",
        "dynamic ",
        "ExpandoObject",
    ):
        assert forbidden not in transaction


def test_native_compile_probe_binds_the_same_public_call_graph() -> None:
    project = (
        REPO
        / "tests/Chummer.Android.Sr5TableWizard.NativeCompile.Tests"
        / "Chummer.Android.Sr5TableWizard.NativeCompile.Tests.csproj"
    ).read_text(encoding="utf-8")
    probe = (
        REPO
        / "tests/Chummer.Android.Sr5TableWizard.NativeCompile.Tests"
        / "TransactionValidationCompileProbe.cs"
    ).read_text(encoding="utf-8")

    assert '<Compile Include="TransactionValidationCompileProbe.cs" />' in project
    assert "new Sr5TableWizardSession().Bind(snapshot)" in probe
    assert "Sr5TableWizardTypedTransactionPresenter.Observe(" in probe
    assert "Sr5TableWizardProjector.ValidateSnapshot" not in probe
