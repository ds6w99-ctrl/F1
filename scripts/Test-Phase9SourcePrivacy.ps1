$ErrorActionPreference='Stop'
$root=(Get-Location).Path
$files=@(Get-ChildItem . -Recurse -File|Where-Object {$_.Extension -in @('.cs','.xaml','.json','.jsonl','.xml','.config','.ps1','.md','.txt','.props','.csproj','.yml','.yaml','.iss','.nuspec') -and $_.Length -lt 16MB})
$patterns=[ordered]@{PrivateKey='-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----';AwsKey='\b(?:AKIA|ASIA)[A-Z0-9]{16}\b';GitHubToken='\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})\b';Jwt='\beyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{20,}\b';CredentialAssignment='(?i)(?:password|api[_-]?key|client[_-]?secret|access[_-]?token)\s*[:=]\s*["''][^"'']{8,}["'']'}
$findings=@()
foreach($file in $files){
 $text=[IO.File]::ReadAllText($file.FullName)
 foreach($rule in $patterns.Keys){
  foreach($match in [regex]::Matches($text,$patterns[$rule])){
   $line=1+([regex]::Matches($text.Substring(0,$match.Index),"`n")).Count
   $findings+=[pscustomobject]@{File=$file.FullName.Substring($root.Length+1);Line=$line;Rule=$rule;Note='Candidate only; matched value deliberately omitted'}
  }
 }
}
$privateFiles=@(Get-ChildItem . -Recurse -File|Where-Object {$_.Extension -in @('.pfx','.p12','.key','.pem')}|ForEach-Object {$_.FullName.Substring($root.Length+1)})
$production=Get-ChildItem src -Recurse -Filter '*.cs'|Where-Object {$_.FullName -notmatch '\\(bin|obj)\\'}
$network=@($production|Select-String -Pattern 'HttpClient|WebClient|WebRequest|TcpClient|UdpClient|Socket\(|Download(File|String)|https?://'|ForEach-Object {[pscustomobject]@{File=$_.Path.Substring($root.Length+1);Line=$_.LineNumber;Text=$_.Line.Trim()}})
$launch=@($production|Select-String -Pattern 'Process.Start|ProcessStartInfo|LoadLibrary|NativeLibrary.Load|DllImport'|ForEach-Object {[pscustomobject]@{File=$_.Path.Substring($root.Length+1);Line=$_.LineNumber;Text=$_.Line.Trim()}})
[ordered]@{Time=[DateTimeOffset]::UtcNow.ToString('o');ScannedTextFiles=$files.Count;MaximumTextBytes=16MB;SecretCandidates=$findings;PrivateKeyFileCandidates=$privateFiles;ProductionNetworkCandidates=$network;LaunchInteropInventory=$launch;Limitations='Pattern-based text scan; binaries and oversized text excluded, no perfect secret detector. Historical dev/controller evidence contains local machine paths; product logs/export have separate allowlist validation.'}|ConvertTo-Json -Depth 6|Set-Content artifacts/phase9/9b/source-privacy-scan.json -Encoding utf8
