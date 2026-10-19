using System;
public static class DeskPagesSimplePreferences {
    public static string[] Build(string language,bool[] basic,bool reverse,bool keyboard,bool reduced,int inbox){
        if(language!="en" && language!="ar" && language!="he")throw new ArgumentException("Invalid language.");if(basic==null || basic.Length!=4 || inbox<0 || inbox>99)throw new ArgumentException("Invalid preferences.");
        return new[]{language,basic[0].ToString(),basic[1].ToString(),basic[2].ToString(),basic[3].ToString(),reverse.ToString(),keyboard.ToString(),reduced.ToString(),inbox.ToString()};
    }
}

