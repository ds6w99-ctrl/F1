$ErrorActionPreference='Stop'
$components=@{};$packages=@{};$edges=@{}
$direct=@('CommunityToolkit.Mvvm','Microsoft.Extensions.DependencyInjection','System.Management','Microsoft.WindowsAppSDK.WinUI','Microsoft.WindowsAppSDK.Runtime','Microsoft.WindowsAppSDK.InteractiveExperiences','Microsoft.Windows.SDK.BuildTools')
foreach($project in @('App','Core','Infrastructure','ElevatedHost')){
 $assets=Get-Content "src/SystemCore.$project/obj/project.assets.json" -Raw|ConvertFrom-Json
 $cache=@($assets.packageFolders.PSObject.Properties.Name)[0]
 foreach($library in $assets.libraries.PSObject.Properties|Where-Object {$_.Value.type -eq 'package'}){
  $name,$version=$library.Name.Split('/')
  $ref="pkg:nuget/$name@$version"
  if(!$components.ContainsKey($ref)){
   $folder=Join-Path $cache $library.Value.path
   $nuspec=Get-ChildItem $folder -Filter '*.nuspec'|Select-Object -First 1
   [xml]$xml=Get-Content $nuspec.FullName -Raw
   $license=$xml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='license']")
   $licenseText=if($license){$license.InnerText}else{$xml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='licenseUrl']").InnerText}
   $kind=if($direct -contains $name){'Direct'}else{'Transitive'}
   $purpose=switch -Regex ($name){'Toolkit' {'MVVM commands and observable state';break};'DependencyInjection' {'Dependency injection and lifecycle';break};'Management|CodeDom' {'Windows WMI inventory support';break};'BuildTools' {'Windows build tooling; not application feature';break};'WebView2' {'WinUI transitive package; no WebView instantiated';break};default {'WinUI / Windows App SDK framework runtime'}}
   $licenseName=if($license -and $license.type -eq 'expression'){$licenseText}elseif($name -match 'BuildTools'){'Microsoft Windows SDK license'}elseif($name -match 'WebView2'){'Microsoft WebView2 license'}else{'Microsoft Windows App SDK binary license'}
   $components[$ref]=[ordered]@{type='library';name=$name;version=$version;'bom-ref'=$ref;purl=$ref;licenses=@(@{license=@{name=$licenseName}});hashes=@(@{alg='SHA-512';content=([BitConverter]::ToString([Convert]::FromBase64String($library.Value.sha512))).Replace('-','').ToLowerInvariant()});properties=@(@{name='systemcore:relationship';value=$kind},@{name='systemcore:purpose';value=$purpose},@{name='systemcore:license-metadata';value=$licenseText})}
   $packages[$ref]=[ordered]@{Package=$name;Version=$version;Purpose=$purpose;Relationship=$kind;License=$licenseName;LicenseMetadata=$licenseText;Status='Development review: no incompatible/GPL dependency identified; original notices retained. Exact final redistribution payload review remains Phase 10.'}
  }
 }
 foreach($target in $assets.targets.PSObject.Properties){
  foreach($library in $target.Value.PSObject.Properties|Where-Object {$_.Value.type -eq 'package'}){
   $name,$version=$library.Name.Split('/');$ref="pkg:nuget/$name@$version"
   if(!$edges.ContainsKey($ref)){$edges[$ref]=@()}
   foreach($dependency in $library.Value.dependencies.PSObject.Properties){
    $match=$target.Value.PSObject.Properties|Where-Object {$_.Name.Split('/')[0] -eq $dependency.Name}|Select-Object -First 1
    if($match){$n,$v=$match.Name.Split('/');$edges[$ref]+= "pkg:nuget/$n@$v"}
   }
  }
 }
}
$runtimeName='Microsoft.NETCore.App.Runtime.win-x64';$runtimeVersion='10.0.12';$runtimeRef="pkg:nuget/$runtimeName@$runtimeVersion"
$runtimeHash=Get-Content (Join-Path $cache 'microsoft.netcore.app.runtime.win-x64/10.0.12/microsoft.netcore.app.runtime.win-x64.10.0.12.nupkg.sha512') -Raw
$components[$runtimeRef]=@{type='library';name=$runtimeName;version=$runtimeVersion;'bom-ref'=$runtimeRef;purl=$runtimeRef;licenses=@(@{license=@{id='MIT'}});hashes=@(@{alg='SHA-512';content=([BitConverter]::ToString([Convert]::FromBase64String($runtimeHash.Trim()))).Replace('-','').ToLowerInvariant()});properties=@(@{name='systemcore:relationship';value='Self-contained runtime pack'})}
$edges[$runtimeRef]=@()
$packages[$runtimeRef]=@{Package=$runtimeName;Version=$runtimeVersion;Purpose='Self-contained .NET runtime';Relationship='Runtime pack';License='MIT with bundled third-party notices';LicenseMetadata='LICENSE.TXT / THIRD-PARTY-NOTICES.TXT';Status='Original runtime notices retained'}
$bom=[ordered]@{bomFormat='CycloneDX';specVersion='1.5';serialNumber='urn:uuid:'+ [Guid]::NewGuid();version=1;metadata=@{timestamp=[DateTimeOffset]::UtcNow.ToString('o');component=@{type='application';name='SystemCore';version='0.9.0-dev'};properties=@(@{name='systemcore:scope';value='Resolved production-project NuGet graph plus self-contained runtime pack. Development Phase 9, not signed final release.'})};components=@($components.Values|Sort-Object name);dependencies=@($edges.Keys|Sort-Object|ForEach-Object{@{ref=$_;dependsOn=@($edges[$_]|Sort-Object -Unique)}})}
$bom|ConvertTo-Json -Depth 15|Set-Content docs/sbom.phase9.cdx.json -Encoding utf8
@($packages.Values|Sort-Object Package)|ConvertTo-Json -Depth 8|Set-Content artifacts/phase9/9b/dependencies.json -Encoding utf8
$lines=@('# Phase 9 dependency review','','| Package | Version | Purpose | Direct/transitive | License |','|---|---|---|---|---|')
foreach($p in $packages.Values|Sort-Object Package){$lines+='| '+$p.Package+' | '+$p.Version+' | '+$p.Purpose+' | '+$p.Relationship+' | '+$p.License+' |'}
$lines+=@('','NuGet vulnerability query (2026-09-25, configured nuget.org feed) returned no listed vulnerabilities. This is feed/tool coverage, not a claim that no undisclosed vulnerabilities exist.','', 'Outdated: Windows SDK BuildTools 10.0.26100.9169 vs 10.0.28000.2705; transitive WebView2 1.0.3719.77 vs 1.0.4191.47 and MSIX BuildTools 1.7.251221100 vs 1.7.260903100. Retained the tested dependency graph: SDK target remains 26100, no product WebView, no reported security advisory requiring an override. No arbitrary transitive version override.','', 'Microsoft-maintained Windows/.NET packages and CommunityToolkit MVVM: no abandoned dependency identified in this review. MIT .NET/Toolkit licensing and Microsoft binary licenses were reviewed; no GPL component was identified in the resolved graph. Original license/notice texts are under docs/licenses. Self-contained .NET runtime 10.0.12 is MIT with its third-party notices retained. Windows itself is an external OS prerequisite. Final exact-payload redistribution and signing/installer-tool licensing remain Release QA.','', 'SBOM: docs/sbom.phase9.cdx.json. Existing historical SBOM files remain unchanged.')
$lines|Set-Content docs/PHASE-9-DEPENDENCIES.md -Encoding utf8
