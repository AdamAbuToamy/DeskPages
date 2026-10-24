$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$source=[IO.File]::ReadAllText((Join-Path $root 'Native/DeskPagesSnapshotCatalog.cs'))
$harness=@'
public static class CatalogFixture {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static void Wait(Func<bool> ready){var watch=System.Diagnostics.Stopwatch.StartNew();while(!ready()&&watch.ElapsedMilliseconds<5000)Thread.Sleep(5);Check(ready(),"Catalog worker did not finish");}
 public static void Run(){
  int ui=Thread.CurrentThread.ManagedThreadId,reads=0;var changed=new System.Collections.Concurrent.ConcurrentQueue<int[]>();
  var values=new System.Collections.Concurrent.ConcurrentDictionary<string,string>();values["a"]="1";values["b"]="1";values["p"]="1";
  using(var c=new DeskPagesSnapshotCatalog(ids=>changed.Enqueue(ids),()=>{},path=>{Check(Thread.CurrentThread.ManagedThreadId!=ui,"Navigation thread read file metadata");Interlocked.Increment(ref reads);Thread.Sleep(5);return values[path];})){
   c.SetPages(new[]{1,2,3});c.SetRows(new[]{"a|1|False","b|2|False","p|1|True"});Wait(()=>c.Digest(3).Length>0);
   int[] startup;while(changed.TryDequeue(out startup)){}
   var first=c.Digest(1);var second=c.Digest(2);int count=reads;
   for(int i=0;i<10000;i++)Check(c.Digest(2)==second,"Digest changed without content change");Check(reads==count,"Digest lookup performed file I/O");
   c.FileChanged("a",1);Wait(()=>c.Digest(1).Length>0);Check(c.Digest(1)==first && changed.IsEmpty,"Duplicate file event invalidated resident images");
   values["a"]="2";c.FileChanged("a",1);Wait(()=>c.Digest(1).Length>0 && c.Digest(1)!=first);int[] ids;Wait(()=>!changed.IsEmpty);Check(changed.TryDequeue(out ids)&&ids.Length==1&&ids[0]==1,"File change invalidated unrelated pages");Check(c.Digest(2)==second,"Page 2 cache stamp changed");
   values["p"]="2";c.FileChanged("p",1);Wait(()=>!changed.IsEmpty);Check(changed.TryDequeue(out ids)&&ids.Length==3,"Pinned icon change did not invalidate all affected pages");
   c.SetRows(new[]{"a|2|False","b|2|False","p|1|True"});Wait(()=>!changed.IsEmpty);Check(changed.TryDequeue(out ids)&&ids.Contains(1)&&ids.Contains(2),"Membership move failed to invalidate source and target");
  }
  Console.WriteLine("PASS: 10,000 zero-I/O stamp lookups, background-only metadata reads, duplicate notifications preserving images, selective file invalidation, pinned images and membership moves.");
 }
}
'@
Add-Type -TypeDefinition ($source+"`n"+$harness) -ReferencedAssemblies @('System.dll','System.Core.dll')
[CatalogFixture]::Run()
