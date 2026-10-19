public static class DeskPagesNavigationPolicy {
    public static bool AllowPreparation(bool explicitUi,bool desktopFocus,bool trayFocus,bool desktopPointer){
        return explicitUi || desktopFocus || trayFocus || desktopPointer;
    }
}
