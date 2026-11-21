using UnityEngine;
using UnityEditor;
using Unity.VectorGraphics;
using TMPro;
using System.Text.RegularExpressions;
using UnityEngine.Rendering.Universal;
using System.IO;
using Unity.VisualScripting;
using System.Drawing;
using UnityEngine.AdaptivePerformance.Provider;
using Codice.Client.Common.GameUI;
using System.Collections.Generic;
using UnityPipeline.System.Reflection.Metadata.Ecma335;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

public class PaintedDepthWindow: EditorWindow //编辑器窗口
{
    [SerializeField] private Camera captureCamera;
    [SerializeField] private int resolution = 1080;
    [SerializeField] private GameObject sceneRoot;

    [SerializeField] private UniversalRendererData captureRenderer;
    
    [SerializeField]private bool genNormal = true;
    [SerializeField]private bool genColor = true;
    [SerializeField]private bool genDepth = true;
    [SerializeField]private bool genNormalEdge = true;
    [SerializeField]private bool genLightNormal = true;
    [SerializeField]private bool genShadowMask = true;
    [SerializeField]private bool genLightContribution = true;
    private bool hasSelection = false;

    [Header("ComfyUI")]

    [SerializeField] private List<string> saveImageNodeIds = new List<string>();
    [SerializeField] private List<string> saveImageNodeLabels = new List<string>();

    private bool scanningParameters;
    private bool showParameterConfiguration = true;
    private Vector2 parameterScroll;
    [SerializeField] private PaintedColorSettings aiSettings =
    new PaintedColorSettings();

    private Texture2D paintedColor;
    private Texture2D livePreview;
    private ComfyPreviewSession activePreview;
    private bool generating;
    private System.Threading.CancellationTokenSource aiCancellation;

    [SerializeField]
    private ComfyBackendSettings backendsettings = new ComfyBackendSettings();

    public enum CaptureChannel
    {
        
    }

     [MenuItem("Tools/PaintedDepth")]
    public static void OpenWindow()
    {
        GetWindow<PaintedDepthWindow>("PaintedDepth");
    }

    private void OnGUI()
    {

        EditorGUILayout.LabelField(
            "SceneBakeTool", EditorStyles.boldLabel
        );//Editor名字

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.BeginVertical(GUILayout.Width(200));//占据左边200
        GUILayout.Label("InfoExporter", EditorStyles.boldLabel);
        captureRenderer = (UniversalRendererData)EditorGUILayout.ObjectField(
            "CaptureRenderer",
            captureRenderer,
            typeof(UniversalRendererData),
            false);//renderer输入s

        captureCamera = (Camera)EditorGUILayout.ObjectField(
            "CaptureCamera",
            captureCamera,
            typeof(Camera),
            true);//scene内camera输入

        
        sceneRoot = (GameObject)EditorGUILayout.ObjectField(
            "SceneRoot",
            sceneRoot,
            typeof(GameObject),
            true);//scene内根对象输入

        resolution = EditorGUILayout.IntPopup(
            "ExportResolution",
            resolution,
            new[]{ "180", "360", "740", "1080"},
            new[]{ 180, 360, 740, 1080});//resolution输入

        EditorGUILayout.LabelField("GenerateSettings", EditorStyles.boldLabel);

        genColor = EditorGUILayout.Toggle("Color Generate",genColor);
        genDepth = EditorGUILayout.Toggle("Depth Generate", genDepth);
        genNormal = EditorGUILayout.Toggle("Normal Generate", genNormal);
        genNormalEdge = EditorGUILayout.Toggle("NormalEdge Generate", genNormalEdge);
        genLightNormal = EditorGUILayout.Toggle("LightNormal Generate", genLightNormal);
        genShadowMask = EditorGUILayout.Toggle("ShdaowMask Generate", genShadowMask);
        genLightContribution = EditorGUILayout.Toggle("LightContribution Generate", genLightContribution);

        hasSelection = 
        genDepth||
        genColor||
        genNormal||
        genNormalEdge||
        genLightNormal||
        genShadowMask||
        genLightContribution;

        EditorGUILayout.Space();

        using(new EditorGUI.DisabledScope(captureCamera == null||sceneRoot == null))
        {
            //按钮锁
            if(GUILayout.Button("SceneTest"))
            {
                Debug.Log("SceneTest 按钮已点击");
                AnalyzeScene();
            }
        }

        EditorGUILayout.LabelField(
            "SceneTest will export basic information of important elemnt, please check your console to confirm these basic information");

        using(new EditorGUI.DisabledScope(captureCamera == null||
        sceneRoot == null||
        captureRenderer == null ||
        !hasSelection))
        {
            if (GUILayout.Button("Export"))
            {
                ExportSelected();
            }
        }
        
        EditorGUILayout.EndVertical();
        EditorGUILayout.BeginVertical();
        GUILayout.Label("AIPainted", EditorStyles.boldLabel);

        GUILayout.Label("AI painted",EditorStyles.boldLabel);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(generating))
        {
            using (new EditorGUI.DisabledGroupScope(scanningParameters))
            {
                aiSettings.serverUrl = EditorGUILayout.TextField(
                    "Server URL",
                    aiSettings.serverUrl
                );

                aiSettings.workflowJson = 
                (TextAsset)EditorGUILayout.ObjectField(
                    "API Workflow",
                    aiSettings.workflowJson,
                    typeof(TextAsset),
                    false
                ); 

                aiSettings.saveImageNodeId = EditorGUILayout.TextField(
                    "SaveImageNodeID",
                    aiSettings.saveImageNodeId
                );

                EditorGUILayout.HelpBox(
                    "ReScan will reset the toggle and value",
                    MessageType.Info
                );
                if(GUILayout.Button("Scan Workflow Parameter"))
                ScanWorkflowParameters();
            }

            using(new EditorGUI.DisabledScope(generating|| scanningParameters))
            {
                var windowData = new SerializedObject(this);
                windowData.Update();

                EditorGUILayout.PropertyField(
                    windowData.FindProperty("backendsettings"),
                    true
                );

                windowData.ApplyModifiedProperties();
            }

            using(new EditorGUI.DisabledScope(generating||scanningParameters))
            {
                if(GUILayout.Button("stop unity run Comfy Backend"))
                {
                    ComfyBackendManager.StopOwned();
                }
            }

            if(GUILayout.Button("Printing Comfy backendLog"))
            {
                Debug.Log(ComfyBackendManager.GetLog());
            }

            if(scanningParameters)
            {
                EditorGUILayout.LabelField("Reading ComfyUI info");
            }
            else if (aiSettings.parametersWorkflow != aiSettings.workflowJson)
            {
                EditorGUILayout.HelpBox(
                    "Workflow has been changed, please scan first",
                    MessageType.Warning
                );
            }
            else
            {
                if(saveImageNodeIds.Count == 0)
                {
                    EditorGUILayout.HelpBox("didnt find saveimage node, please add it on Comfy and reexport and scan",MessageType.Warning);
                }
                else if(saveImageNodeIds.Count ==1)
                {
                    EditorGUILayout.LabelField("ExportPic", saveImageNodeLabels[0]);
                }
                else
                {
                    int current = saveImageNodeIds.IndexOf(aiSettings.saveImageNodeId);
                    int selected = EditorGUILayout.Popup(
                        "export Pic",
                        current,
                        saveImageNodeLabels.ToArray()

                    );

                    if(selected >= 0)
                    {
                        aiSettings.saveImageNodeId = saveImageNodeIds[selected];
                    }
                }
                DrawWorkflowParameters();
            }

            if (GUILayout.Button("Generate PaintedColor"))
            GeneratePaintedColor();
        }
        if(generating)
        {
            EditorGUILayout.LabelField("Generating……");

            if (GUILayout.Button("StopWaiting"))
            aiCancellation?.Cancel();
        }

        Texture2D displayedImage = generating ? livePreview : paintedColor;

        if(displayedImage != null)
        {
            EditorGUILayout.LabelField(
                generating? "Preview" : "Result",
                EditorStyles.boldLabel
            );

            Rect rect = GUILayoutUtility.GetAspectRect(
                (float)displayedImage.width/displayedImage.height
            );

            GUI.DrawTexture(
                rect,
                displayedImage,
                ScaleMode.ScaleToFit
            );
        }
        else if (generating)
        {
            EditorGUILayout.LabelField("waiting for preview");
        }
        

        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }
        private void AnalyzeScene()
    {
        //检测renderer数量
        MeshRenderer[] renderers = 
        sceneRoot.GetComponentsInChildren<MeshRenderer>(true);//true会检测scene未激活对象

        string projection = captureCamera.orthographic? "ortho":"persp";//ortho检测

        Debug.Log(
            $"场景：{sceneRoot.name}\n" +
            $"MeshRenderer 数量：{renderers.Length}\n" +
            $"相机：{captureCamera.name}（{projection}）\n" +
            $"计划输出：{resolution} × {resolution}",
            sceneRoot);
    }

    private void ExportSelected()
    {
        ScriptableRendererFeature depthFeature = captureRenderer.rendererFeatures.Find(
            feature => feature != null&&
            feature.name =="DepthCapture");
        ScriptableRendererFeature normalFeature = captureRenderer.rendererFeatures.Find(
            feature => feature != null&&
            feature.name =="NormalCapture");
        ScriptableRendererFeature edgeFeature = captureRenderer.rendererFeatures.Find(
            feature => feature != null&&
            feature.name =="NormalEdgeCapture");
        ScriptableRendererFeature lightFeature = captureRenderer.rendererFeatures.Find(
            feature => feature != null&&
            feature.name == "LightNormalCapture");
        ScriptableRendererFeature shadowFeature = captureRenderer.rendererFeatures.Find(
            feature => feature != null&&
            feature.name == "ShadowMaskCapture");
        ScriptableRendererFeature lightConFeature = captureRenderer.rendererFeatures.Find(
            feature => feature != null&&
            feature.name == "LightContributionCapture" );
        if(depthFeature == null || normalFeature == null||
        edgeFeature == null || lightFeature == null|| shadowFeature == null|| lightConFeature == null)
        {
            Debug.LogError("no required features");
            return;
        }

        string folder = EditorUtility.OpenFolderPanel("Choose Folder",
        "",
        "");//导出文件夹选择

        if(string.IsNullOrEmpty(folder))
        return;

        string batchName = "Capture_" +
        System.DateTime.Now.ToString("yyyyMMdd_HHmmss") +
        "_" +
        System.Guid.NewGuid().ToString("N").Substring(0, 8);

        string outputFolder = Path.Combine(folder, batchName);
        Directory.CreateDirectory(outputFolder);//选择存储文件位置

        bool previousDepth = depthFeature.isActive;
        bool previousNormal = normalFeature.isActive;
        bool previousEdge = edgeFeature.isActive;
        bool previousLight = lightFeature.isActive;
        bool previousShadow = shadowFeature.isActive;
        bool previousLightContribution = lightConFeature.isActive;

       // 每次采集都明确设置三个 Feature，避免互相覆盖。
        void Capture(
            bool depth,
            bool normal,
            bool edge,
            bool light,
            bool shadow,
            bool lightcon,
            string fileName)
        {
            depthFeature.SetActive(depth);
            normalFeature.SetActive(normal);
            edgeFeature.SetActive(edge);
            lightFeature.SetActive(light);
            shadowFeature.SetActive(shadow);

            SceneCapture.ExportColor(
                captureCamera,
                resolution,
                Path.Combine(outputFolder, fileName));
        }

        try
        {
            if (genColor)
                Capture(false, false, false, false, false, false, "Color.png");

            if (genDepth)
                Capture(true, false, false, false, false, false, "DepthPreview.png");

            if (genNormal)
                Capture(false, true, false, false, false, false, "NormalPreview.png");
            if (genNormalEdge)
                Capture(false, false, true, false, false, false, "NormalEdge.png");
            if(genLightNormal)
                Capture(false, false, false, true, false, false, "NormalLight.png" );
            if(genShadowMask)
                Capture(false, false, false, false, true, false, "ShadowMask.png");
            if(genLightContribution)
                Capture(false, false, false, false, false, true, "LightGen.png");
        }
        finally
        {
            depthFeature.SetActive(previousDepth);
            normalFeature.SetActive(previousNormal);
            edgeFeature.SetActive(previousEdge);
            lightFeature.SetActive(previousLight);
            shadowFeature.SetActive(previousShadow);
            lightConFeature.SetActive(previousLightContribution);

        }
    }

    private async void GeneratePaintedColor()
    {
        if(generating)
        return;

        if(scanningParameters||aiSettings.workflowJson == null||
        aiSettings.parametersWorkflow != aiSettings.workflowJson)
        {
            Debug.LogError("please scan first");
            return;
        }

        var imagePath = new Dictionary<string, string>();
        int width = 0;
        int height = 0;

        foreach (var parameter in aiSettings.parameters)
        {
            if(!parameter.exposed|| parameter.type != WorkflowParameterType.Image)
            continue;//如果不是图片跳过

            Texture2D image = parameter.imageValue;
            if(image == null)
            {
                Debug.LogError($"please choose pic:{parameter.displayName}");
                return;
            }

            string path = AssetDatabase.GetAssetPath(image);
            if(!File.Exists(path))
            {
                Debug.LogError($"please choose pic file in Proejct:{parameter.displayName}");
                return;
            }

            if(imagePath.Count == 0)
            {
                width = image.width;
                height = image.height;
            }

            imagePath.Add(parameter.nodeId, path);
        }

        if(imagePath.Count == 0)
        {
            Debug.LogError("please choose at least one pic to input");
            return;
        }
        generating = true;
        ClearLivePreview();


        var cancellation =
            new System.Threading.CancellationTokenSource();

        

        var previewSession = new ComfyPreviewSession();
        aiCancellation = cancellation;
        activePreview = previewSession;

        try
        {
            Debug.Log("starting comfy backend");

            await ComfyBackendManager.EnsureRunningAsync(
                backendsettings,
                aiSettings.serverUrl,
                cancellation.Token
            );

            cancellation.Token.ThrowIfCancellationRequested();

            Debug.Log("submitting task");

            byte[] bytes = await PaintedColorClient.GenerateAsync(
                aiSettings,
                imagePath,
                cancellation.Token
            );

            cancellation.Token.ThrowIfCancellationRequested();
            
            if(this == null)
            return;

            paintedColor = PaintedColorImporter.Import(bytes, width, height);

            Debug.Log("PaintedColor successfully generated");

        }
        catch(System.OperationCanceledException)
        {
            Debug.Log("Stopped");
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            cancellation.Dispose();

            if (this != null)
           {
                activePreview = null;
                aiCancellation = null;
                generating = false;
                Repaint();
            }
        }
    }

    private async void ScanWorkflowParameters()
    {
        if(scanningParameters|| generating)//如果在进行其他的进程就返回
        return;

        if(aiSettings.workflowJson == null)
        {
            Debug.LogError("Please assign APIWorkflow JSON first");
            return;
        }

        TextAsset source = aiSettings.workflowJson;
        string workflowText = source.text;
        string serverUrl = aiSettings.serverUrl;//信息配对

        scanningParameters = true;//开始进程

        try
        {
            await ComfyBackendManager.EnsureRunningAsync(
               backendsettings,
               serverUrl,
               System.Threading.CancellationToken.None
            );

            var parameters = await WorkflowParameterTools.ScanAsync(
              workflowText,
              serverUrl
            );

            if(this == null)
            return;

            if(aiSettings.workflowJson != source||
            aiSettings.serverUrl != serverUrl)
            return;

            ScanSvaeImageNodes(workflowText, aiSettings.parametersWorkflow == source);
            aiSettings.parameters = parameters;
            aiSettings.parametersWorkflow = source;//替换

            Debug.Log($"Scan finished, find {parameters.Count} nodes that able to edit");
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            if(this != null)
            {
                scanningParameters = false;
                Repaint();
            }
        }

    }

    private void DrawWorkflowParameters()
    {
        EditorGUILayout.LabelField("Picture input",EditorStyles.boldLabel);

        bool hasImageInputs = false;

        foreach (WorkflowParameter parameter in aiSettings.parameters)
        {
           if(!parameter.exposed||
           parameter.type != WorkflowParameterType.Image)
            {
                continue;
            } 
            WorkflowParameterTools.DrawValue(parameter);
            hasImageInputs = true;
        }
        if(!hasImageInputs)
        {
            EditorGUILayout.HelpBox(
                "choose your LoadImgae inputs in below", MessageType.Info
            );
        }
        showParameterConfiguration = EditorGUILayout.Foldout(
            showParameterConfiguration,
            "Configuration",
            true
        );

        parameterScroll = EditorGUILayout.BeginScrollView(
            parameterScroll,
            GUILayout.Height(300)
        );

        try
        {
            foreach (WorkflowParameter parameter in aiSettings.parameters)//扫描parameter
            {
                if(showParameterConfiguration)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    try
                    {
                        EditorGUILayout.LabelField(
                            $"Node{parameter.nodeId}: { parameter.classType}"
                        );
                        EditorGUILayout.LabelField(
                            "Inputs",
                            parameter.inputName
                        );

                        parameter.exposed = EditorGUILayout.Toggle(
                            "showininterface",
                            parameter.exposed
                        );

                        if(parameter.exposed)
                        {
                            parameter.displayName = 
                            EditorGUILayout.TextField(
                                "Name",
                                parameter.displayName
                            );
                        }
                    }
                    finally
                    {
                        EditorGUILayout.EndVertical();
                    }
                }
            }
        }
        finally
        {
            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField("generate parameter",EditorStyles.boldLabel);
            foreach(WorkflowParameter parameter in aiSettings.parameters)
            {
                if(parameter.exposed &&
                parameter.type != WorkflowParameterType.Image)
                {
                    WorkflowParameterTools.DrawValue(parameter);
                }
            }
        }
    }

    private void ScanSvaeImageNodes(string workflowText, bool sameWorkflow)
    {
        JObject workflow = JObject.Parse(workflowText);

        saveImageNodeIds.Clear();
        saveImageNodeLabels.Clear();

        foreach (JProperty entry in workflow.Properties())
        {
            if(!(entry.Value is JObject node)||
            (string)node["class_type"]!= "SaveImage")
            {
                continue;
            }

            string title = (string)node["_meta"]?["title"]?? "Save Image";
            saveImageNodeIds.Add(entry.Name);
            saveImageNodeLabels.Add($"{title} (#{entry.Name})");
        }

        if(saveImageNodeIds.Count == 1)
        {
            aiSettings.saveImageNodeId = saveImageNodeIds[0];
        }
        else if (!sameWorkflow ||
        !saveImageNodeIds.Contains(aiSettings.saveImageNodeId))
        {
            aiSettings.saveImageNodeId = "";
        }
    }

    private void OnEnable()
    {
        EditorApplication.update += UpdateLivePreview;
    }
    private void OnDisable()
    {
        EditorApplication.update -= UpdateLivePreview;

        aiCancellation?.Cancel();
        activePreview?.Abort();

        ClearLivePreview();
    }

    private void UpdateLivePreview()
    {
        if(!generating || activePreview == null)
        return;

        string warning = activePreview.TakeWarning();

        if(!string.IsNullOrEmpty(warning))
        Debug.LogWarning(warning);

        byte[] imageBytes = activePreview.TakeLatestImage();

        if(imageBytes == null)
        return;

        if(livePreview == null)
        {
            livePreview = new Texture2D(
                
                2,
                2,
                TextureFormat.RGBA32,
                false   
            );
            livePreview.hideFlags = HideFlags.HideAndDontSave;
        }

        if(ImageConversion.LoadImage(livePreview,imageBytes))
        {
            Repaint();
        }
        else
        {
            Debug.LogWarning("received but unable to decode");
        }
    }

    private void ClearLivePreview()
    {
        if(livePreview == null)
        return;
        
        DestroyImmediate(livePreview);
        livePreview = null;
    }
   
    
}
