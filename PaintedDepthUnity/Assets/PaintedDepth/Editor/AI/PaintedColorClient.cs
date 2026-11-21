
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Codice.CM.Common.Merge;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AppUI.Navigation;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using System.Collections.Generic;

public class PaintedColorClient
{
    public static async Task<byte[]> GenerateAsync(
        PaintedColorSettings settings,
        IReadOnlyDictionary<string,string> imagePath,
        CancellationToken cancellationToken,
        ComfyPreviewSession previewSession = null)
    {
        if(settings == null|| settings.workflowJson ==null)
        throw new ArgumentException("please set API workflow Json");

        if(!Uri.TryCreate(
            settings.serverUrl.TrimEnd('/') + "/",
            UriKind.Absolute,
            out Uri serverUri)||
            (serverUri.Scheme != "http" &&
             serverUri.Scheme != "https"))
        {
            throw new ArgumentException("unavailable ComfyUI address");
        }
        //以上全部为输入和设置的安全检测

        JObject workflow = JObject.Parse(settings.workflowJson.text);
        if (workflow["nodes"] != null)
        {
            throw new ArgumentException(" this is normal workflowJson please export as API form");
        }//检测Json，如果有nodes说明是UI格式需要改为API
        WorkflowParameterTools.Apply(workflow, settings.parameters);
        GetInputs(workflow, settings.saveImageNodeId);
        string outputNodeId = settings.saveImageNodeId;
        // output传入


        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        CancellationToken token = timeout.Token;

        using var client = new HttpClient
        {
            BaseAddress = serverUri,
            Timeout = TimeSpan.FromMinutes(2)

        };
        //设置超时

        foreach(var entry in imagePath)
        {
            string nodeId = entry.Key;
            JObject inputs = GetInputs(workflow,nodeId, "image");
            if((string)workflow[nodeId]?["class_type"]!="LoadImage"||
            inputs["image"].Type != JTokenType.String)
            {
                throw new ArgumentException($"node{nodeId}has change its pic input, please rescan");
            }

            inputs["image"] = await UploadImageAsync(client,entry.Value, token);
        }//检测并提交所有的images

       string clientId = Guid.NewGuid().ToString("N");

       if(previewSession != null)
        {
            await previewSession.ConnectAsync(
                serverUri,
                clientId,
                token
            );
        }

        var payload = new JObject
        {
            ["prompt"] = workflow,
            ["client_id"] = clientId
        };

        using var body = new StringContent(
            payload.ToString(Formatting.None),//字符串格式转化
            Encoding.UTF8,//编码选择
            "application/Json");//格式告知
        using var submitResponse = await client.PostAsync("prompt", body, token);//提交异步数据
        string submitText = await ReadCheckedAsync(submitResponse);//读取返回ID
        JObject submitted = JObject.Parse(submitText);//Json解析成JObject
        string promptId = (string)submitted["prompt_id"];//提取prompt id内容

        if(string.IsNullOrEmpty(promptId))
        throw new Exception("failed" + submitText);//任务效果检查

        while(true)//轮询 类似Update
        {
            token.ThrowIfCancellationRequested();//检测是否取消任务+后期可以加一个取消的按钮

            using var historyResponse = await client.GetAsync(
                "history/" + Uri.EscapeDataString(promptId),
                token);//查询历史，escapedata是保护措施，编码需要编码的字符
            
            JObject history = JObject.Parse(
                await ReadCheckedAsync(historyResponse));//解析成JObject

            JToken job = history[promptId];

            if(job != null)//如果history查找存在
            {
                JToken status = job["status"];
                if((string)status?["status_str"]=="error")
                throw new Exception("ComfyUI failed" + status);//error检查

                JToken image = job["outputs"]?[outputNodeId]?["images"]?.First;
                if(image != null)
                {
                    string filename = (string)image["filename"];
                    if(string.IsNullOrEmpty(filename))
                    throw new Exception("lost image name");//取图片filename

                    string query = 
                    "view?filename=" +
                    Uri.EscapeDataString(filename) + 
                    "&subfolder=" +
                    Uri.EscapeDataString(
                        (string)image["subfolder"] ?? "") +
                        "&type=" +
                    Uri.EscapeDataString(
                        (string)image["type"] ?? "output");//comfyUI接口参数拼接
                    
                    using var imageResponse = 
                    await client.GetAsync(query, token);

                    if(!imageResponse.IsSuccessStatusCode)
                    await ReadCheckedAsync (imageResponse);

                    return await imageResponse.Content.ReadAsByteArrayAsync();//取图片
                        
            
                }

                if ((bool?)status?["completed"] == true)
                {
                    throw new Exception(
                        "任务完成，但指定输出节点没有图片。" +
                        "请检查 SaveImage 节点 ID。");
                }
            }

            await Task.Delay(1000, token);//等待下一轮

            
        }


    }

    private static JObject GetInputs(
        JObject workflow,
        string nodeId,
        params string[] fields)
    {
        if(string.IsNullOrWhiteSpace(nodeId))
        throw new ArgumentException("unknown ID");

        JObject inputs = workflow[nodeId]?["inputs"] as JObject;//取inputs

        if(inputs == null)
        throw new ArgumentException("cant find node");

        foreach (string field in fields)
        {
            if (inputs.Property(field)== null)
            {
                throw new ArgumentException("nodeId havent inputs field");
            }
        }//遍历判断是否存在

        return inputs;
    }

    private static async Task<string> UploadImageAsync(
        HttpClient client,
        string path,
        CancellationToken token)//本地图片上传
    {
        using var form = new MultipartFormDataContent();//multipart表单容器

        string name =
        Guid.NewGuid().ToString("N") + Path.GetExtension(path);//创建名字

        form.Add(
            new ByteArrayContent(File.ReadAllBytes(path)),
            "image",
            name
        );//读文件到内存

        form.Add(new StringContent("input"),"type");

        using var response = 
        await client.PostAsync("upload/image", form, token);//post

        JObject uploaded = JObject.Parse(
            await ReadCheckedAsync(response)
        );//解析

        string filename = (string)uploaded["name"];
        string subfolder = (string)uploaded["subfolder"];

        if(string.IsNullOrEmpty(filename))
        throw new Exception("fail uploaded");

        return string.IsNullOrEmpty(subfolder)
        ?filename
        :subfolder.TrimEnd('/') +"/"+filename;//存入
    }

    private static async Task<string> ReadCheckedAsync(
        HttpResponseMessage response
    )//读响应
    {
        string text = await response.Content.ReadAsStringAsync();

        if(!response.IsSuccessStatusCode)
        {
            throw new Exception($"HTTP {(int)response.StatusCode}：{text}");
            
        }
        return text;
    }
}
