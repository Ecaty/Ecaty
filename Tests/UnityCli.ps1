# Compatible with Windows PowerShell 5.1 and PowerShell 7. Preserve argv without a shell.
function ConvertTo-NativeArgument([AllowEmptyString()][string]$Value) {
    if($Value.Length -gt 0 -and $Value -notmatch '[\s"]'){return $Value}
    $builder=New-Object Text.StringBuilder
    [void]$builder.Append('"');$slashes=0
    foreach($character in $Value.ToCharArray()) {
        if($character -eq '\'){$slashes++;continue}
        if($character -eq '"'){[void]$builder.Append(('\' * (2*$slashes+1)));[void]$builder.Append('"')}else{[void]$builder.Append(('\' * $slashes));[void]$builder.Append($character)}
        $slashes=0
    }
    [void]$builder.Append(('\' * (2*$slashes)));[void]$builder.Append('"');return $builder.ToString()
}
function Invoke-UnityProcess([string]$Executable,[string[]]$Arguments) {
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=$Executable
    $info.Arguments=($Arguments | ForEach-Object {ConvertTo-NativeArgument $_}) -join ' '
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $info.StandardOutputEncoding=[Text.Encoding]::UTF8;$info.StandardErrorEncoding=[Text.Encoding]::UTF8
    $process=New-Object Diagnostics.Process;$process.StartInfo=$info
    try {
        if(-not $process.Start()){throw 'Could not start Unity CLI.'}
        $output=$process.StandardOutput.ReadToEndAsync();$errors=$process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{Code=$process.ExitCode;Output=$output.GetAwaiter().GetResult();Error=$errors.GetAwaiter().GetResult()}
    } finally {$process.Dispose()}
}
