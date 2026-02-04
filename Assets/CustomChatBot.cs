using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Assets
{
    /*
     * Message
     * ------
     * Represents a single chat message shown in the UI.
     */
    public class Message
    {
        public string Text;
        public Text TextObject;
        public MessageType MessageType;
    }

    /*
     * MessageType
     * -----------
     */
    public enum MessageType
    {
        User,
        Bot
    }

    /*
     * CustomChatBot
     * -----------
     * Controls:
     * - Chat UI behavior
     * - Message storage
     * - Rule based chatbot logic from external file
     */
    public class CustomChatBot : MonoBehaviour
    {
        /* ---------------- UI REFERENCES ---------------- */
        public GameObject chatPanel;
        public GameObject textObject;
        public InputField chatBox;

        /* ---------------- VISUAL SETTINGS ---------------- */
        public Color UserColor;
        public Color BotColor;

        /* ---------------- INTERNAL STATE ---------------- */
        private readonly List<Message> Messages = new List<Message>();
        private List<Rule> rules = new List<Rule>();

        // 存储 fallback 的特殊回复
        private string fallbackResponse = "I did not understand that. Try typing: help.";

        private void Start()
        {
            // 从 Resources/ChatBotData.txt 加载数据
            LoadRulesFromFile();

            // Initial bot message when the chat starts
            AddMessage("Bot: Ready. Type a message and press Enter.", MessageType.Bot);
        }

        /*
         * LoadRulesFromFile()
         * -------------------
         * 从 Resources 文件夹读取 txt 文件并解析规则。
         * 这样无需更改代码即可扩展对话内容。
         */
        private void LoadRulesFromFile()
        {
            // 加载文件（不需要加 .txt 后缀）
            TextAsset dataFile = Resources.Load<TextAsset>("ChatBotData");
            if (dataFile == null)
            {
                Debug.LogError("ChatBotData file not found in Resources folder!");
                return;
            }

            // 按行分割
            string[] lines = dataFile.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                // 寻找分隔符 '|'
                if (!line.Contains("|")) continue;

                string[] parts = line.Split('|');

                // 处理关键词部分：移除 标记并按逗号分割
                string keywordsPart = parts[0];
                if (keywordsPart.Contains("]"))
                {
                    keywordsPart = keywordsPart.Substring(keywordsPart.IndexOf(']') + 1);
                }

                string[] keywords = keywordsPart.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                string response = parts[1].Trim();

                // 检查是否是 fallback 规则
                bool isFallback = false;
                foreach (string k in keywords)
                {
                    if (k.Trim().ToLower() == "fallback") isFallback = true;
                }

                if (isFallback)
                {
                    fallbackResponse = response;
                }
                else
                {
                    rules.Add(new Rule(keywords, response));
                }
            }
        }

        public void AddMessage(string messageText, MessageType messageType)
        {
            if (Messages.Count >= 25)
            {
                Destroy(Messages[0].TextObject.gameObject);
                Messages.RemoveAt(0);
            }

            var newMessage = new Message
            {
                Text = messageText,
                MessageType = messageType
            };

            var newText = Instantiate(textObject, chatPanel.transform);
            newMessage.TextObject = newText.GetComponent<Text>();
            newMessage.TextObject.text = messageText;
            newMessage.TextObject.color = messageType == MessageType.User ? UserColor : BotColor;

            Messages.Add(newMessage);
        }

        public void SendMessageToBot()
        {
            var userMessage = chatBox.text;
            if (string.IsNullOrWhiteSpace(userMessage)) return;

            AddMessage($"User: {userMessage}", MessageType.User);
            string botReply = GetBotResponse(userMessage);
            AddMessage($"Bot: {botReply}", MessageType.Bot);

            chatBox.Select();
            chatBox.text = "";
        }

        private string GetBotResponse(string userMessage)
        {
            string msg = userMessage.Trim().ToLowerInvariant();

            foreach (var rule in rules)
            {
                if (rule.Matches(msg))
                    return rule.Response;
            }

            return fallbackResponse;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                SendMessageToBot();
            }
        }

        [Serializable]
        private class Rule
        {
            public string[] Keywords;
            public string Response;

            public Rule(string[] keywords, string response)
            {
                Keywords = keywords ?? Array.Empty<string>();
                Response = response ?? "";
            }

            public bool Matches(string normalizedMessage)
            {
                foreach (var keyword in Keywords)
                {
                    if (normalizedMessage.Contains(keyword.Trim().ToLowerInvariant()))
                        return true;
                }
                return false;
            }
        }
    }
}