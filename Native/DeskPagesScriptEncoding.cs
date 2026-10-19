using System;
using System.IO;
using System.Text;

// Shared by the compiled installer and test suite. Validate bytes before any
// PowerShell process starts, independent of the machine's ANSI code page.
public static class DeskPagesScriptEncoding {
    public static void Validate(byte[] bytes){
        if(bytes==null || bytes.Length==0)throw new InvalidDataException("Empty PowerShell script.");
        bool bom=bytes.Length>=3 && bytes[0]==0xef && bytes[1]==0xbb && bytes[2]==0xbf;
        bool nonAscii=false;
        foreach(byte value in bytes){if(value==0)throw new InvalidDataException("PowerShell scripts must be UTF-8, not UTF-16 or binary.");if(value>=128)nonAscii=true;}
        try{new UTF8Encoding(false,true).GetString(bytes);}
        catch(DecoderFallbackException){throw new InvalidDataException("PowerShell script contains invalid UTF-8 bytes.");}
        if(nonAscii && !bom)throw new InvalidDataException("Non-ASCII PowerShell script must have a UTF-8 BOM for Windows PowerShell 5.1.");
    }
    public static int ValidateDirectory(string directory){
        int count=0;
        foreach(string path in Directory.GetFiles(directory,"*.ps1",SearchOption.AllDirectories)){
            try{Validate(File.ReadAllBytes(path));}
            catch(InvalidDataException ex){throw new InvalidDataException("Invalid script encoding: "+Path.GetFileName(path)+". "+ex.Message);}
            count++;
        }
        return count;
    }
}
