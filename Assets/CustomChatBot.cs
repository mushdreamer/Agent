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

        private readonly List<Message> Messages = new List<Message>();
        private List<Rule> rules = new List<Rule>();
        private string fallbackResponse = "I'm sorry, I didn't quite catch that.";

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
            var userMessage = chatBox.text;
            if (string.IsNullOrWhiteSpace(userMessage)) return;

            AddMessage($"User: {userMessage}", MessageType.User);

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

                // --- 动画触发逻辑 ---
                if (characterAnimator != null)
                {
                    string triggerToUse = successTrigger; // 默认

                    // 根据关键词微调动作
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
                        Debug.Log($"[ChatBot Debug] 触发特定动作: {triggerToUse}");
                    }
                    else
                    {
                        characterAnimator.SetTrigger(successTrigger);
                        Debug.LogWarning($"[ChatBot Debug] 找不到 '{triggerToUse}'，回退到通用动作 '{successTrigger}'");
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
                        Debug.Log("[ChatBot Debug] 触发 Fallback 动作");
                    }
                });
            }

            chatBox.text = "";
            chatBox.Select();
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