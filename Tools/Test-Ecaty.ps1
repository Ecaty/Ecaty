param([ValidateSet('Native','Gallery','EditMode','PlayMode','Resources','Validator','All')][string]$Suite='Gallery')
# Compatibility entry; the test runner now lives with the other test commands.
& (Join-Path $PSScriptRoot '../Tests/Run.ps1') -Suite $Suite
