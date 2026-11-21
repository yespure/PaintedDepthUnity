using UnityEngine;
using System;
using System.Collections.Generic;

[Serializable]
public class PaintedColorSettings 
{
   public string serverUrl = "http://127.0.0.1:8188";
   //comfyUI地址

   public TextAsset workflowJson;
    // 从 ComfyUI 导出的 API 格式 JSON
    public string saveImageNodeId;

    public List<WorkflowParameter> parameters =
    new List<WorkflowParameter>();

// 记录这份参数列表属于哪个工作流。
   public TextAsset parametersWorkflow;
}
