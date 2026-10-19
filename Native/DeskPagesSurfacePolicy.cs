// Hit-testing uses the actual window and its GA_ROOT, never popup ownership.
public static class DeskPagesSurfacePolicy
{
    public static bool IsDesktopSurface(string hit,string root,bool originalDesktopRoot){
        bool surface=hit=="SysListView32" || hit=="SHELLDLL_DefView" || hit=="Progman" || hit=="WorkerW";
        bool desktop=root=="Progman" || root=="WorkerW" || originalDesktopRoot;
        return surface && desktop;
    }
}
