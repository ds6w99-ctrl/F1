param([Parameter(Mandatory=$true)][string]$ScriptPath,[Parameter(Mandatory=$true)][string]$LogPath)
$ErrorActionPreference='Stop'
# Diagnostics launcher only. Duplicate the interactive Explorer token to LOWER
# test privileges when this harness itself is elevated. No production reference.
if (!('SystemCoreDesktopValidation' -as [type])) {
 Add-Type @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
public static class SystemCoreDesktopValidation {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup {
  public int cb; public string reserved,desktop,title;
  public int x,y,width,height,xChars,yChars,fill,flags;
  public short show,reserved2;public IntPtr bytes,input,output,error;
 }
 [StructLayout(LayoutKind.Sequential)] struct ProcessInfo {public IntPtr process,thread;public int pid,tid;}
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool DuplicateTokenEx(IntPtr token,uint access,IntPtr attributes,int level,int type,out IntPtr duplicate);
 [DllImport("userenv.dll",SetLastError=true)] static extern bool CreateEnvironmentBlock(out IntPtr environment,IntPtr token,bool inherit);
 [DllImport("userenv.dll")] static extern bool DestroyEnvironmentBlock(IntPtr environment);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcessWithTokenW(IntPtr token,uint logon,string application,StringBuilder command,uint flags,IntPtr environment,string directory,ref Startup startup,out ProcessInfo info);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll",SetLastError=true)] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetExitCodeProcess(IntPtr process,out uint exitCode);
 public static int Run(string executable,string arguments,string directory) {
  int session;using(var self=Process.GetCurrentProcess())session=self.SessionId;
  var explorers=Process.GetProcessesByName("explorer");
  try {
   var shell=explorers.First(p=>p.SessionId==session);
   IntPtr token;if(!OpenProcessToken(shell.Handle,0xB,out token))throw new Win32Exception(Marshal.GetLastWin32Error());
   try {
    IntPtr duplicate;if(!DuplicateTokenEx(token,0x02000000,IntPtr.Zero,2,1,out duplicate))throw new Win32Exception(Marshal.GetLastWin32Error());
    try {
     IntPtr environment;if(!CreateEnvironmentBlock(out environment,duplicate,false))throw new Win32Exception(Marshal.GetLastWin32Error());
     try {
      var startup=new Startup {cb=Marshal.SizeOf(typeof(Startup)),desktop="winsta0\\default"};ProcessInfo info;
      if(!CreateProcessWithTokenW(duplicate,0,executable,new StringBuilder("\""+executable+"\" "+arguments),0x400,environment,directory,ref startup,out info))throw new Win32Exception(Marshal.GetLastWin32Error(),"CreateProcessWithTokenW failed");
      try {
       var wait=WaitForSingleObject(info.process,1200000);
       if(wait==258)throw new TimeoutException("Desktop validation still running; no process was killed");
       if(wait!=0)throw new Win32Exception(Marshal.GetLastWin32Error());
       uint exitCode;if(!GetExitCodeProcess(info.process,out exitCode))throw new Win32Exception(Marshal.GetLastWin32Error());
       return unchecked((int)exitCode);
      }
      finally {CloseHandle(info.thread);CloseHandle(info.process);}
     }finally {DestroyEnvironmentBlock(environment);}
    }finally {CloseHandle(duplicate);}
   }finally {CloseHandle(token);}
  }finally {foreach(var process in explorers)process.Dispose();}
 }
}
'@
}
$directory=(Get-Location).Path
$log=[IO.Path]::GetFullPath($LogPath)
$script=(Resolve-Path $ScriptPath).Path.Replace("'","''")
# CreateProcessWithTokenW has a 1,024-character command-line limit. Load a trusted
# diagnostics script through a short in-memory entry point instead of encoding
# the entire pipeline. Execution policy is not changed.
$body="try { & ([scriptblock]::Create([IO.File]::ReadAllText('$script'))) *> '$($log.Replace("'","''"))'; exit 0 } catch { `$_ | Out-File '$($log.Replace("'","''"))' -Append; exit 1 }"
$arguments='-NoProfile -NonInteractive -Command "'+$body+'"'
$exit=[SystemCoreDesktopValidation]::Run("$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe",$arguments,$directory)
if($exit -ne 0){throw "Desktop-token validation failed ($exit). See $LogPath"}
