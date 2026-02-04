using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class PurdueGenAIStreamingClient : MonoBehaviour
{
    [Header("RCAC GenAI Studio")]
    [SerializeField] private string apiKey = "PASTE_YOUR_API_KEY";
    [SerializeField] private string url = "https://genai.rcac.purdue.edu/api/chat/completions";
    [SerializeField] private string model = "llama3.1:latest";

    [Header("Admin (System Instructions)")]
    [TextArea(3, 10)]
    [SerializeField] private string adminInstructions = "";

    // Internal conversation memory (single source of truth)
    private readonly List<ChatMessage> messages = new List<ChatMessage>();

    /// <summary>
    /// Public read-only access for external systems (logger, analytics, etc.)
    /// </summary>
    public IReadOnlyList<ChatMessage> Messages => messages;

    /// <summary>
    /// Clears the conversation.
    /// The next user message will include the admin/system instructions again.
    /// </summary>
    public void ResetConversation()
    {
        messages.Clear();
    }

    /// <summary>
    /// Sends a streaming chat request.
    /// Admin/system message is added ONLY if this is the first message.
    /// </summary>
    public void SendStreaming(
        string userText,
        Action<string> onDelta,
        Action<string> onDone,
        Action<string> onError = null)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            onError?.Invoke("User message is empty.");
            return;
        }

        // Add admin/system only once at the beginning
        if (messages.Count == 0 && !string.IsNullOrWhiteSpace(adminInstructions))
        {
            messages.Add(new ChatMessage
            {
                role = "system",
                content = adminInstructions
            });
        }

        // Add user message
        messages.Add(new ChatMessage
        {
            role = "user",
            content = userText
        });

        StartCoroutine(StreamCoroutine(onDelta, onDone, onError));
    }

    // ================= STREAMING CORE =================

    private IEnumerator StreamCoroutine(
        Action<string> onDelta,
        Action<string> onDone,
        Action<string> onError)
    {
        var requestBody = new ChatRequest
        {
            model = model,
            stream = true,
            messages = messages
        };

        byte[] bodyRaw = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestBody));
        string fullResponse = "";

        var downloadHandler = new SseDownloadHandler((jsonLine) =>
        {
            if (string.IsNullOrWhiteSpace(jsonLine)) return;
            if (jsonLine == "[DONE]") return;

            try
            {
                JObject chunk = JObject.Parse(jsonLine);
                string delta = chunk["choices"]?[0]?["delta"]?["content"]?.ToString();

                if (!string.IsNullOrEmpty(delta))
                {
                    fullResponse += delta;
                    onDelta?.Invoke(delta);
                }
            }
            catch
            {
                // Ignore malformed or partial chunks
            }
        });

        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = downloadHandler;

        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "text/event-stream");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            string details = request.downloadHandler != null ? request.downloadHandler.text : "";
            onError?.Invoke($"HTTP {request.responseCode}: {request.error}\n{details}");
            yield break;
        }

        // Store assistant reply for conversation context
        messages.Add(new ChatMessage
        {
            role = "assistant",
            content = fullResponse
        });

        onDone?.Invoke(fullResponse);
    }

    // ================= DATA STRUCTURES =================

    [Serializable]
    private class ChatRequest
    {
        public string model;
        public bool stream;
        public List<ChatMessage> messages;
    }

    [Serializable]
    public class ChatMessage
    {
        // Standard roles: system, user, assistant
        public string role;
        public string content;
    }

    // ================= SSE HANDLER =================

    private class SseDownloadHandler : DownloadHandlerScript
    {
        private readonly Action<string> onData;
        private readonly StringBuilder buffer = new StringBuilder();

        public SseDownloadHandler(Action<string> onData, int bufferSize = 1024)
            : base(new byte[bufferSize])
        {
            this.onData = onData;
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength <= 0) return false;

            buffer.Append(Encoding.UTF8.GetString(data, 0, dataLength));

            while (true)
            {
                string current = buffer.ToString();
                int splitIndex = current.IndexOf("\n\n", StringComparison.Ordinal);

                if (splitIndex < 0)
                    break;

                string block = current.Substring(0, splitIndex);
                buffer.Remove(0, splitIndex + 2);

                foreach (string line in block.Split('\n'))
                {
                    if (line.StartsWith("data:"))
                    {
                        onData?.Invoke(line.Substring(5).Trim());
                    }
                }
            }

            return true;
        }
    }
}
