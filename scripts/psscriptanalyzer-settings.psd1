@{
    # The single definition of the script lint settings. CI and the review gate both run
    # Invoke-ScriptAnalyzer -Settings with this file (ci.yml and scripts/review-gate.ps1), so a violation fails locally the same way it fails in CI.
    Severity = @('Warning', 'Error')
}
