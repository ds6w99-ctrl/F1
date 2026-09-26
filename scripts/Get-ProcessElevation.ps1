param([Parameter(Mandatory=$true)][int]$ProcessId)
if (!('SystemCoreValidationToken' -as [type])) {
 Add-Type @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class SystemCoreValidationToken {
 [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int info,out int value,int length,out int returned);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 public static bool IsElevated(int pid) {
  var process=OpenProcess(0x1000,false,pid);
  if(process==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
  try {
   IntPtr token;
   if(!OpenProcessToken(process,8,out token))throw new Win32Exception(Marshal.GetLastWin32Error());
   try {int value,length;if(!GetTokenInformation(token,20,out value,4,out length))throw new Win32Exception(Marshal.GetLastWin32Error());return value!=0;}
   finally {CloseHandle(token);}
  }finally {CloseHandle(process);}
 }
}
'@
}
[SystemCoreValidationToken]::IsElevated($ProcessId)
