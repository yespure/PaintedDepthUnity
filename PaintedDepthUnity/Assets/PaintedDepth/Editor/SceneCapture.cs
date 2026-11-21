using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


public static class SceneCapture 
{
    public static void ExportColor(Camera camera, int resolution, string path)
    {
        if(camera == null)
        {
            Debug.Log("Need to assign Camera");
            return;
        }//camera安全检查 不必要可以删


        // 用户取消保存时，不进行采集。
        if (string.IsNullOrEmpty(path))
            return;


        // 保存原来的状态。
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        //set
        RenderTexture render = null;
        Texture2D texture = null;
        int height = resolution;
        int width = resolution/9*16;


        try
        {
            render = new RenderTexture(
                width,
                height,
                24,
                RenderTextureFormat.ARGB32
            );
            render.Create();//创建GPU资源
            //camera.targetTexture = render;//定位
            //camera.Render();//渲染一帧
            var request = new UniversalRenderPipeline.SingleCameraRequest
            {
                destination = render
            };

            if(!RenderPipeline.SupportsRenderRequest(camera, request))
            {
                Debug.Log("Render Pass Unsupport");
                return;
            }//pass安全检查

            RenderPipeline.SubmitRenderRequest(camera, request);//渲染请求

            RenderTexture.active = render;

            texture = new Texture2D(width,height,TextureFormat.RGBA32,false);
            texture.ReadPixels(new Rect (0,0,width,height),0,0);//读pixel
            texture.Apply();
            //写入
            TextureExporter.ExportFile(texture, path);
        }

        finally
        {
            //全部reset
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            //数据清空
            if(render != null)
            {
                render.Release();
                Object.DestroyImmediate(render);
            }

            if (texture != null)
            {
                Object.DestroyImmediate(texture);
            }
        }

    }

}
