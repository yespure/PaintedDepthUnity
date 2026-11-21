using System;

[Serializable]
public class ComfyBackendSettings
{
    public bool autoStart = true;

    public string pythonPath = "/Users/michaelli/ComfyUI-Installs/ComfyUI/" +
        "standalone-env/bin/python";
    
    public string comfyDirectory =  "/Users/michaelli/ComfyUI-Installs/ComfyUI/ComfyUI";

    public string extraModelPathConfig =  "/Users/michaelli/Library/Application Support/" +
        "Comfy Desktop/instance-model-paths/inst-1792520092434.yaml";
        //comfy自带模型搜索，基本不用调试
    
    public int startupTimeoutSeconds = 120;
}

