using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Net.Http;
using System.Runtime.Remoting.Messaging;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.AppUI.Navigation;
using Unity.VisualScripting.YamlDotNet.Core.Tokens;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

public enum WorkflowParameterType
{
    Text,
    Integer,
    FLoat,
    Boolean,
    Choice,
    Image
}

[Serializable]
public class WorkflowParameter
{
    public string nodeId;
    public string classType;
    public string inputName;
    public string displayName;
    public bool exposed;
    public Texture2D imageValue;
    public WorkflowParameterType type;
    [Header("typebased")]

    public string textValue;
    public float floatValue;
    public bool boolValue;
    public bool useSlider;
    public float min;
    public float max;

    public string[] choices;
}

public static class WorkflowParameterTools
{
    public static async Task<List<WorkflowParameter>> ScanAsync(
        string workflowText,
        string serverUrl)
    {
        var workflow = JObject.Parse(workflowText);
        if(workflow["nodes"]!= null)
        {
            throw new ArgumentException("need API form workflow");
        }

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        //timeout 和安全检查
        
        string url = serverUrl.TrimEnd('/') + "/object_info";

        var schema = JObject.Parse(await client.GetStringAsync(url));

        var result = new List<WorkflowParameter>();
        //遍历node
        foreach(JProperty nodeProperty in workflow.Properties())
        {
            if(!(nodeProperty.Value is JObject node))
            continue;//如果value是object 赋值
            //提取type
            string classType = (string)node["class_type"];

            if(string.IsNullOrEmpty(classType))
            continue;
            //检测inputs
            if(!(node["inputs"] is JObject inputs))
            continue;

            string title = 
            (string)node["_meta"]?["title"]??classType;//优先使用自定义标题，如果没有就用classtype

            foreach(JProperty input in inputs.Properties())//遍历所有输入参数
            {
                if(!(input.Value is JValue))
                continue;//只查找JValue

                JToken definitions = schema[classType]?["input"];//def为schema 每个node通过classtype找input

                JArray definition = definitions?["required"]?[input.Name] as JArray ??
                definitions?["optional"]?[input.Name] as JArray;//从输入中找参数并转化成JArray

                var parameter = new WorkflowParameter
                {
                    nodeId = nodeProperty.Name,
                    classType = classType,
                    inputName = input.Name,
                    displayName = title + "/" + input.Name,
                    exposed = false,
                    textValue = input.Value.ToString()
                };//构建初始化对象

                if(classType == "LoadImage" &&
                input.Name == "image" &&
                input.Value.Type == JTokenType.String)
                {
                    parameter.type = WorkflowParameterType.Image;
                    parameter.displayName = $"{title} (#{nodeProperty.Name})";
                }
                else if(definition != null &&
                definition.Count > 0 &&
                definition[0] is JArray options &&
                input.Value.Type == JTokenType.String)//满足所有条件才是choice
                {
                    var strings =new List<string>();
                    bool allStrings = true;//假设所有选项都是字符串
                    foreach (JToken option in options)
                    {
                        if(option.Type != JTokenType.String)
                        {
                            allStrings = false;
                            break;
                        }

                        strings.Add(option.Value<string>());//如果有转化为字符串并加入
                    }

                    if(!allStrings || strings.Count == 0)//？？？这段感觉可以重新优化
                    continue;//再检查

                    parameter.type = WorkflowParameterType.Choice;
                    parameter.choices = strings.ToArray();//标记为自己的choice
                    
                }
                else
                {
                    string declaredType =
                        definition != null &&
                        definition.Count > 0 &&
                        definition[0].Type == JTokenType.String
                            ? definition[0].Value<string>()
                            : null;//提取type信息

                    if(declaredType == "FLOAT"|| input.Value.Type == JTokenType.Float)
                    {
                        parameter.type = WorkflowParameterType.FLoat;
                        parameter.floatValue = input.Value.Value<float>();

                        JObject limits = definition != null &&
                        definition.Count >1 
                        ? definition[1] as JObject
                        : null;

                        if(limits?["min"]!= null &&
                        limits?["max"]!= null)
                        {
                            parameter.min = limits["min"].Value<float>();
                            parameter.max = limits["max"].Value<float>();

                            parameter.useSlider = 
                            parameter.max > parameter.min &&
                            !float.IsInfinity(parameter.min)&&
                            !float.IsInfinity(parameter.max)&&
                            !float.IsNaN(parameter.min) &&
                            !float.IsNaN(parameter.max);
                        }//通过最大最小值设定slider
                    }
                    else if(input.Value.Type == JTokenType.Integer)
                    {
                        parameter.type = WorkflowParameterType.Integer;
                    }
                    else if(input.Value.Type == JTokenType.Boolean)
                    {
                        parameter.type = WorkflowParameterType.Boolean;
                        parameter.boolValue = input.Value.Value<bool>();         
                    }
                    else if (input.Value.Type == JTokenType.String)
                    {
                        parameter.type = WorkflowParameterType.Text;
                    }
                    else
                    {
                        continue;
                    }

                    
                }
                result.Add(parameter);
            }
        }
        return result;
    }
//以下函数用于根据参数类型，自动绘制控件
    public static void DrawValue(WorkflowParameter parameter)
    {
        string label = parameter.displayName;

        switch(parameter.type)
        {
            case WorkflowParameterType.Image:
            parameter.imageValue = (Texture2D)EditorGUILayout.ObjectField(
                label,
                parameter.imageValue,
                typeof(Texture2D),
                false
            );
            break;
            
            case WorkflowParameterType.Text:
            EditorGUILayout.LabelField(label);
            parameter.textValue = EditorGUILayout.TextArea(
                parameter.textValue??"",
                GUILayout.MinHeight(42)
            );
            break;

            case WorkflowParameterType.Integer:
            parameter.textValue = EditorGUILayout.TextField(label, parameter.textValue??"0");
            break;

            case WorkflowParameterType.FLoat:
            parameter.floatValue = parameter.useSlider?EditorGUILayout.Slider(
                label, 
                parameter.floatValue, 
                parameter.min,
                parameter.max): 
            EditorGUILayout.FloatField(label, parameter.floatValue);
            break;

            case WorkflowParameterType.Boolean:
            parameter.boolValue = EditorGUILayout.Toggle(
                label,
                parameter.boolValue
            );
            break;

            case WorkflowParameterType.Choice:
            string[]options = parameter.choices;

            if(options ==null||options.Length == 0)
            break;


            int current = Array.IndexOf(
                options,
                parameter.textValue
            );

            if(current < 0)
            {
                EditorGUILayout.HelpBox(label +" are not in the choice", MessageType.Warning);
            }

            EditorGUI.BeginChangeCheck();

            int selected = EditorGUILayout.Popup(
                label,
                current,
                options
            );

            if(EditorGUI.EndChangeCheck()&& selected >= 0)
            parameter.textValue = options[selected];

            break;


        }
    }


    public static void Apply(
        JObject workflow,
        List<WorkflowParameter> parameters)
    {
        if(parameters == null)
        return;

        foreach(WorkflowParameter parameter in parameters)
        {
            if(!parameter.exposed)
            continue;

            JObject node = workflow[parameter.nodeId] as JObject;
            JObject inputs = node?["inputs"] as JObject;

            if(inputs == null||
            (string)node["class_type"] != parameter.classType||
            inputs.Property(parameter.inputName) == null)
            {
                throw new ArgumentException($"parameter fail to connect: {parameter.displayName}. please rescan the workflow.");
            }

            if(!(inputs[parameter.inputName] is JValue))
            {
                throw new ArgumentException($"inputs changed to a complex data: {parameter.displayName}");
            }

            JToken value;

            switch (parameter.type)
            {
                case WorkflowParameterType.Integer:
                value = JToken.Parse(parameter.textValue);

                if(value.Type != JTokenType.Integer)
                throw new ArgumentException(parameter.displayName + "must be vaild integer.");
                break;

                case WorkflowParameterType.FLoat:
                if(float.IsNaN(parameter.floatValue)||
                float.IsInfinity(parameter.floatValue))
                {
                    throw new ArgumentException(parameter.displayName + "must be vaild float");
                        
                }

                value = new JValue(parameter.floatValue);
                break;

                case WorkflowParameterType.Boolean:
                value = new JValue(parameter.boolValue);
                break;

                case WorkflowParameterType.Choice:
                if(parameter.choices == null ||
                Array.IndexOf(
                    parameter.choices,
                    parameter.textValue) < 0)
                {
                    throw new ArgumentException(parameter.displayName +" invaild choice")   ; 
                }

                value = new JValue(parameter.textValue);
                break;

                default:
                value = new JValue(parameter.textValue ?? "");
                break;
                
            }

            inputs[parameter.inputName] = value;
        }
    }

}