using UnityEngine;
using System.IO;
using UnityEditor;

public static class TextureExporter 
{
    /*
    public static string ChoosePngPath(string defaultName)
    {
        return EditorUtility.SaveFilePanel(
            "Export Png",
            "",
            defaultName,
            "png");//设置path
    }
    */
    public static void ExportFile(Texture2D texture, string path)
    {
        if(texture == null)
        throw new System.ArgumentNullException(nameof(texture));

        if(string.IsNullOrEmpty(path))//两个安全检查
        throw new System.ArgumentException("path cant be empty", nameof(path));


        byte[] bytes = texture.EncodeToPNG();//密码成PNG
        File.WriteAllBytes(path, bytes);//File写上
    }
}
