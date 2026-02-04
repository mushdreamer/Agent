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
        [Tooltip("拖入场景中虚拟角色的 Animator 组件")]
        public Animator characterAnimator;
        [Tooltip("当机器人理解并给出确切回答时触发的 Trigger 名称")]
        public string successTrigger = "TalkSuccess";
        [Tooltip("当机器人不理解或进入 LLM 模式时触发的 Trigger 名称")]
        public string fallbackTrigger = "TalkFallback";

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
                    Debug.LogError("[ChatBot Debug] 找不到 Animator 组件！请检查是否已将角色拖入 Inspector 或子物体中是否有 Animator。");
                }
                else
                {
                    Debug.Log("[ChatBot Debug] 自动在子物体中找到了 Animator: " + characterAnimator.name);
                }
            }
            else
            {
                Debug.Log("[ChatBot Debug] 已手动分配 Animator: " + characterAnimator.name);
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
            if (dataFile == null)
            {
                Debug.LogError("[ChatBot Debug] 找不到 ChatBotData 资源文件，请确保它在 Resources 文件夹下。");
                return;
            }

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
            Debug.Log($"[ChatBot Debug] 已加载 {rules.Count} 条对话规则。");
        }

        public void SendMessageToBot()
        {
            var userMessage = chatBox.text;
            if (string.IsNullOrWhiteSpace(userMessage)) return;

            AddMessage($"User: {userMessage}", MessageType.User);
            Debug.Log($"[ChatBot Debug] 收到用户消息: {userMessage}");

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
                Debug.Log($"[ChatBot Debug] 匹配成功！命中关键词: {bestAudioKey}。准备触发动画: {successTrigger}");
                AddMessage($"Bot: {matchedRule.Response}", MessageType.Bot);

                if (!audioDatabase.ContainsKey(bestAudioKey))
                    bestAudioKey = matchedRule.Keywords[0].Trim().ToLower();

                PlayAudioForIntent(bestAudioKey);

                if (characterAnimator != null)
                {
                    if (HasParameter(characterAnimator, successTrigger))
                    {
                        characterAnimator.SetTrigger(successTrigger);
                    }
                    else
                    {
                        Debug.LogWarning($"[ChatBot Debug] 警告：Animator 中没有名为 '{successTrigger}' 的 Trigger 参数！请检查 Animator 窗口。");
                    }
                }
            }
            else
            {
                Debug.Log("[ChatBot Debug] 未匹配到本地规则，发送请求给 LLM。准备触发动画: " + fallbackTrigger);
                llmClient.SendStreaming(userMessage, null, (fullResponse) =>
                {
                    AddMessage($"Bot: {fullResponse}", MessageType.Bot);
                    PlayAudioForIntent("fallback");

                    if (characterAnimator != null)
                    {
                        if (HasParameter(characterAnimator, fallbackTrigger))
                        {
                            characterAnimator.SetTrigger(fallbackTrigger);
                        }
                        else
                        {
                            Debug.LogWarning($"[ChatBot Debug] 警告：Animator 中没有名为 '{fallbackTrigger}' 的 Trigger 参数！");
                        }
                    }
                });
            }

            chatBox.text = "";
            chatBox.Select();
        }

        private bool HasParameter(Animator animator, string paramName)
        {
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