// Explorer focus/show/hide notifications do not change the cached desktop image.
public static class DeskPagesSnapshotInvalidation {
 public static bool ChangesImage(uint evt){return evt==0x8000 || evt==0x8001 || evt==0x8004 || evt==0x800b || evt==0x800c || evt==0x8015;}
}
