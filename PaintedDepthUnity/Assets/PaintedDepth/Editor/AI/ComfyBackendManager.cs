using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using PlasticGui;
using Unity.AppUI.Navigation;
using UnityEditor;

public static class ComfyBackendManager
{
    private  static readonly SemaphoreSlim startLock = new SemaphoreSlim(1,1);

    private static Process ownedProcess;
    private static string ownedServerUrl;
    private static readonly StringBuilder log = new StringBuilder();
    private static readonly object logLock = new object();
    static ComfyBackendManager()
    {
        AssemblyReloadEvents.beforeAssemblyReload += StopOwned;
        //保证ownedProcess的接收持续
        EditorApplication.quitting += StopOwned;
    }//关于退出的构造函数

    public static async Task EnsureRunningAsync(
        ComfyBackendSettings settings,
        string serverUrl,
        CancellationToken token
    )
    {
        if(settings == null)
        throw new ArgumentNullException(nameof(settings));

        if(!Uri.TryCreate(
            serverUrl,
            UriKind.Absolute,
            out Uri serverUri)||
            serverUri.Scheme !="http"||
            !serverUri.IsLoopback ||
            serverUri.AbsolutePath != "/"||
            !string.IsNullOrEmpty(serverUri.Query)||
            !string.IsNullOrEmpty(serverUri.Fragment)||
            !string.IsNullOrEmpty(serverUri.UserInfo
        ))
        {
            throw new ArgumentException(
                "Please use correct address"
            );
        }

        await startLock.WaitAsync(token);
        //启动并防止同步冲突
        try
        {
            token.ThrowIfCancellationRequested();
            //有服务时直接复用，不启动第二份
            if(await IsReadyAsync(serverUri, token))
            return;

            if(!settings.autoStart)
            {
                throw new InvalidOperationException(
                    "Comfy didnt work"
                );
            }

            string address =  serverUri.AbsoluteUri;

            if(ownedProcess != null && ownedProcess.HasExited)
            {
                ownedProcess.Dispose();
                ownedProcess = null;
                ownedServerUrl = null;
            }//检查是否有运行的process

            if(ownedProcess != null && ownedServerUrl != address)
            {
                throw new InvalidOperationException(
                    "Unity already stared a ComfyUI" +"please stopped it first and change the address."
                );
            }//重复检查，如果有启动的项目而且并不是对应的地址的话就报错

            if(ownedProcess == null)
            {
                StartProcess(settings, serverUri);
                ownedServerUrl = address;
            }//如果没有启动的进程那么启动并同步地址

            DateTime deadline = DateTime.UtcNow.AddSeconds(
                Math.Max(10, settings.startupTimeoutSeconds)
            );//设定timeout时间

            while(DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();

                if(ownedProcess.HasExited)
                {
                    throw new InvalidOperationException(
                        "Comfy exited\n" + GetLog()
                    );
                }

                if(await IsReadyAsync(serverUri, token))
                return;

                await Task.Delay(1000, token);
            }//检查comfy是否正常启动

            throw new TimeoutException(
                "Comfy timeout\n" + GetLog()
            );
        }
        finally
        {
            startLock.Release();
        }
    }

    private static void StartProcess(
        ComfyBackendSettings settings,
        Uri serverUri
    )//comfy的后台启动进程
    {
        if(!File.Exists(settings.pythonPath))
        throw new FileNotFoundException(
            "cant find Python",settings.pythonPath
        );

        string mainPath = Path.Combine(settings.comfyDirectory, "main.py");
        //细化路径选择定位main.py的路径

        if(!File.Exists(mainPath))
        throw new FileNotFoundException(
            "Cant find mainpy from ComfyUI", mainPath
        );

        string arguments = 
        "-u " + Quote(mainPath) +
            " --listen 127.0.0.1" +
            " --port " + serverUri.Port +
            " --disable-auto-launch"+
            " --preview-method latent2rgb";
            //arugments用于具体调用，127.0.0.1用于本地调用，
            //如果想要局域网调用的话就改成0.0.0.0
            //disable保证后台调用
            
        if(!string.IsNullOrWhiteSpace(settings.extraModelPathConfig))
        {//如果存在额外模型
            if(!File.Exists(settings.extraModelPathConfig))
            {
                throw new FileNotFoundException("cant find extra model path config", settings.extraModelPathConfig);
            }//通过comfy内置搜索模型进行额外模型文件搜索
            arguments += " --extra-model-paths-config " +
                Quote(settings.extraModelPathConfig);
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = settings.pythonPath,          
                Arguments = arguments,                   
                WorkingDirectory = settings.comfyDirectory, 
                UseShellExecute = false,                 // 不走系统 shell，必须 false 才能重定向
                CreateNoWindow = true,                   // 不弹控制台黑窗口
                RedirectStandardOutput = true,           // 捕获 stdout
                RedirectStandardError = true             // 捕获 stderr
            }
        };

        lock(logLock)
        log.Clear();

        // 这些回调在后台线程执行，只保存文本
        process.OutputDataReceived +=
            (_, e) => AppendLog(e.Data);

        process.ErrorDataReceived +=
            (_, e) => AppendLog(e.Data);
        //订阅日志与报错

        try
        {
            if(!process.Start())
            throw new InvalidOperationException("unable to start Comfy");
        }
        catch
        {
            process.Dispose();
            throw;
        }//正式启动comfy进程

        ownedProcess = process;

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        //失败检查日志

    }

    private static async Task<bool> IsReadyAsync(
        Uri serverUri,
        CancellationToken token
    )
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(token);

        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        using var client = new HttpClient();
        //防过载超时部分，和其他防超时逻辑同理

        try
        {
            using var response = await client.GetAsync(
                new Uri(serverUri, "system_stats"),
                timeout.Token
            );//get async

            if(!response.IsSuccessStatusCode)//安全检查
            return false;

            string text = await response.Content.ReadAsStringAsync();

            JObject result = JObject.Parse(text);
            return result["system"] != null &&
                   result["devices"] != null;
        }
        catch (OperationCanceledException)
        {
            token.ThrowIfCancellationRequested();
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return false;
        }
    }

    //以下为整合的简单工具method
    private static string Quote(string value)
    {
        if(value.Contains("\"")||
        value.Contains("\n")||
        value.Contains("\r"))
        {
            throw new ArgumentException("start path contains invalid text");
        }
        return "\"" + value + "\"";
    }

    private static void AppendLog(string line)
    {
        if(string.IsNullOrEmpty(line))
        return;

        lock(logLock)
        {
            log.AppendLine(line);

            // 只保留最近的日志。
            if (log.Length > 12000)
                log.Remove(0, log.Length - 12000);
        }
    }

    public  static string GetLog()
    {
        lock(logLock)
        return log.ToString();
    }

    public static void StopOwned()
    {
        Process process = ownedProcess;
        if(process == null)
        return;

        try
        {
            if (!process.HasExited)
                process.Kill();

            process.Dispose();

            ownedProcess = null;
            ownedServerUrl = null;
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning(
                "failed to stopped Comfy backend" + exception.Message);
        }
    }

}