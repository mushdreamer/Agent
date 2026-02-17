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

        [Header("Animation Settings")]
        public Animator characterAnimator;
        [Tooltip("通用成功动作")]
        public string successTrigger = "TalkSuccess";
        [Tooltip("没听懂/LLM动作")]
        public string fallbackTrigger = "TalkFallback";
        [Tooltip("打招呼动作 (hello/hi)")]
        public string helloTrigger = "TalkHello";
        [Tooltip("再见动作 (bye/thanks)")]
        public string byeTrigger = "TalkBye";
        [Tooltip("重复问题时的动作")]
        public string repeatTrigger = "TalkFallback";

        private readonly List<Message> Messages = new List<Message>();
        private List<Rule> rules = new List<Rule>();
        private string fallbackResponse = "I'm sorry, I didn't quite catch that.";

        // --- 记忆机制变量 ---
        private List<string> userQuestionHistory = new List<string>();

        private void Start()
        {
            audioSource = GetComponent<AudioSource>();
            LoadRulesFromFile();
            LoadAudioAssets();

            if (characterAnimator == null)
            {
                characterAnimator = GetComponentInChildren<Animator>();
                if (characterAnimator == null)
                {
                    Debug.LogError("[ChatBot Debug] 错误：依然找不到 Animator！请手动将角色模型拖入 Inspector 窗口的 Character Animator 槽位中。");
                }
            }

            AddMessage("Bot: Tennis Coach is online.", MessageType.Bot);
        }

        private void LoadAudioAssets()
        {
            AudioClip[] allClips = Resources.LoadAll<AudioClip>("Audio");
            foreach (var clip in allClips)
            {
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
            var userMessage = chatBox.text.Trim();
            if (string.IsNullOrWhiteSpace(userMessage)) return;

            AddMessage($"User: {userMessage}", MessageType.User);

            // --- 记忆机制：检查重复提问 ---
            string lowerMessage = userMessage.ToLower();
            if (userQuestionHistory.Contains(lowerMessage))
            {
                HandleRepeatQuestion();
                chatBox.text = "";
                chatBox.Select();
                return;
            }

            userQuestionHistory.Add(lowerMessage);

            Rule matchedRule = null;
            string bestAudioKey = null;

            foreach (var rule in rules)
            {
                foreach (var k in rule.Keywords)
                {
                    string cleanKey = k.Trim().ToLower();
                    if (userMessage.ToLower().Contains(cleanKey))
                    {
                        matchedRule = rule;
                        bestAudioKey = cleanKey;
                        break;
                    }
                }
                if (matchedRule != null) break;
            }

            if (matchedRule != null)
            {
                AddMessage($"Bot: {matchedRule.Response}", MessageType.Bot);

                if (!audioDatabase.ContainsKey(bestAudioKey))
                    bestAudioKey = matchedRule.Keywords[0].Trim().ToLower();

                PlayAudioForIntent(bestAudioKey);

                if (characterAnimator != null)
                {
                    string triggerToUse = successTrigger;

                    if (bestAudioKey.Contains("hello") || bestAudioKey.Contains("hi") || bestAudioKey.Contains("hey"))
                    {
                        triggerToUse = helloTrigger;
                    }
                    else if (bestAudioKey.Contains("bye") || bestAudioKey.Contains("thanks"))
                    {
                        triggerToUse = byeTrigger;
                    }

                    if (HasParameter(characterAnimator, triggerToUse))
                    {
                        characterAnimator.SetTrigger(triggerToUse);
                    }
                    else
                    {
                        characterAnimator.SetTrigger(successTrigger);
                    }
                }
            }
            else
            {
                llmClient.SendStreaming(userMessage, null, (fullResponse) =>
                {
                    AddMessage($"Bot: {fullResponse}", MessageType.Bot);
                    PlayAudioForIntent("fallback");

                    if (characterAnimator != null && HasParameter(characterAnimator, fallbackTrigger))
                    {
                        characterAnimator.SetTrigger(fallbackTrigger);
                    }
                });
            }

            chatBox.text = "";
            chatBox.Select();
        }

        // --- 修改后的重复处理逻辑 ---
        private void HandleRepeatQuestion()
        {
            // 这里你可以修改 text 让它和你做的音频内容完全一致
            string repeatResponse = "You've already asked that! Please try saying something else.";
            AddMessage($"Bot: {repeatResponse}", MessageType.Bot);

            // 尝试播放 key 为 "repeat" 的音频
            // 请确保你的音频文件名以 "repeat" 开头，例如 "repeat"
            PlayAudioForIntent("repeat");

            if (characterAnimator != null && HasParameter(characterAnimator, repeatTrigger))
            {
                characterAnimator.SetTrigger(repeatTrigger);
                Debug.Log("[ChatBot Debug] 播放重复提问的专属音频和动作。");
            }
        }

        private bool HasParameter(Animator animator, string paramName)
        {
            if (animator == null) return false;
            foreach (AnimatorControllerParameter param in animator.parameters)
            {
                if (param.name == paramName) return true;
            }
            return false;
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
                // 如果找不到 repeat 音频，会回退到 fallback
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