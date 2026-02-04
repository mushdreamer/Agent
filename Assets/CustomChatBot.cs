using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;

namespace Assets
{
    public class Message
    {
        public string Text;
        public Text TextObject;
        public MessageType MessageType;
    }

    public enum MessageType { User, Bot }

    [RequireComponent(typeof(AudioSource))]
    public class CustomChatBot : MonoBehaviour
    {
        public GameObject chatPanel;
        public GameObject textObject;
        public InputField chatBox;
        public Color UserColor;
        public Color BotColor;

        [Header("Integrations")]
        public PurdueGenAIStreamingClient llmClient;
        private AudioSource audioSource;
        private Dictionary<string, List<AudioClip>> audioDatabase = new Dictionary<string, List<AudioClip>>();

        private readonly List<Message> Messages = new List<Message>();
        private List<Rule> rules = new List<Rule>();
        private string fallbackResponse = "I'm sorry, I didn't quite catch that.";

        private void Start()
        {
            audioSource = GetComponent<AudioSource>();
            LoadRulesFromFile();
            LoadAudioAssets();
            AddMessage("Bot: Tennis Coach is online.", MessageType.Bot);
        }

        private void LoadAudioAssets()
        {
            // 加载 Audio 文件夹下的所有资源
            AudioClip[] allClips = Resources.LoadAll<AudioClip>("Audio");
            foreach (var clip in allClips)
            {
                // 统一转小写处理
                string key = clip.name.ToLower();
                if (key.Contains("_")) key = key.Split('_')[0];

                if (!audioDatabase.ContainsKey(key)) audioDatabase[key] = new List<AudioClip>();
                audioDatabase[key].Add(clip);
            }
        }

        private void LoadRulesFromFile()
        {
            TextAsset dataFile = Resources.Load<TextAsset>("ChatBotData");
            if (dataFile == null) return;

            string[] lines = dataFile.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                if (!line.Contains("|")) continue;
                string[] parts = line.Split('|');
                string keywordsPart = parts[0];
                if (keywordsPart.Contains("]")) keywordsPart = keywordsPart.Substring(keywordsPart.IndexOf(']') + 1);

                string[] keywords = keywordsPart.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                string response = parts[1].Trim();

                if (keywords.Any(k => k.Trim().ToLower() == "fallback")) fallbackResponse = response;
                else rules.Add(new Rule(keywords, response));
            }
        }

        public void SendMessageToBot()
        {
            var userMessage = chatBox.text;
            if (string.IsNullOrWhiteSpace(userMessage)) return;

            AddMessage($"User: {userMessage}", MessageType.User);

            Rule matchedRule = null;
            string bestAudioKey = null;

            // 核心修复：遍历规则时，记录下匹配到的那个关键词
            foreach (var rule in rules)
            {
                foreach (var k in rule.Keywords)
                {
                    string cleanKey = k.Trim().ToLower();
                    if (userMessage.ToLower().Contains(cleanKey))
                    {
                        matchedRule = rule;
                        bestAudioKey = cleanKey; // 记录匹配到的关键词用于播放音频
                        break;
                    }
                }
                if (matchedRule != null) break;
            }

            if (matchedRule != null)
            {
                AddMessage($"Bot: {matchedRule.Response}", MessageType.Bot);
                // 修正：优先使用关键词匹配音频，如果关键词没音频，则尝试用该 rule 的第一个关键词
                if (!audioDatabase.ContainsKey(bestAudioKey))
                    bestAudioKey = matchedRule.Keywords[0].Trim().ToLower();

                PlayAudioForIntent(bestAudioKey);
            }
            else
            {
                // 调用 LLM 时播放 fallback 音频
                llmClient.SendStreaming(userMessage, null, (fullResponse) =>
                {
                    AddMessage($"Bot: {fullResponse}", MessageType.Bot);
                    PlayAudioForIntent("fallback");
                });
            }

            chatBox.text = "";
            chatBox.Select();
        }

        private void PlayAudioForIntent(string intent)
        {
            if (audioDatabase.ContainsKey(intent))
            {
                var clips = audioDatabase[intent];
                audioSource.clip = clips[UnityEngine.Random.Range(0, clips.Count)];
                audioSource.Play();
            }
            else if (audioDatabase.ContainsKey("fallback"))
            {
                audioSource.clip = audioDatabase["fallback"][0];
                audioSource.Play();
            }
        }

        public void AddMessage(string messageText, MessageType messageType)
        {
            var newText = Instantiate(textObject, chatPanel.transform);
            var msg = new Message { TextObject = newText.GetComponent<Text>() };
            msg.TextObject.text = messageText;
            msg.TextObject.color = (messageType == MessageType.User) ? UserColor : BotColor;
            Messages.Add(msg);
        }

        private void Update() { if (Input.GetKeyDown(KeyCode.Return)) SendMessageToBot(); }

        [Serializable]
        private class Rule
        {
            public string[] Keywords;
            public string Response;
            public Rule(string[] k, string r) { Keywords = k; Response = r; }
        }
    }
}