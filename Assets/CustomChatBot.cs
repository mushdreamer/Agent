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
     * Each message stores:
     * - The text itself
     * - A reference to the UI Text object that displays it
     * - Whether it was sent by the user or the bot
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
     * Used to distinguish between user messages and bot messages.
     * This allows us to render them with different colors.
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
     * - Rule based chatbot logic
     *
     * This version uses NO external chatbot library.
     * It relies purely on keyword matching.
     */
    public class CustomChatBot : MonoBehaviour
    {
        /* ---------------- UI REFERENCES ---------------- */

        // Panel where chat messages are added
        public GameObject chatPanel;

        // Prefab containing a Text component for each message
        public GameObject textObject;

        // Input field where the user types
        public InputField chatBox;

        /* ---------------- VISUAL SETTINGS ---------------- */

        // Color for user messages
        public Color UserColor;

        // Color for bot messages
        public Color BotColor;

        /* ---------------- INTERNAL STATE ---------------- */

        // List storing the most recent messages
        private readonly List<Message> Messages = new List<Message>();

        /*
         * rules
         * -----
         * Each rule defines:
         * - A set of keywords to look for in the user's message
         * - A response the bot should send if a keyword matches
         *
         * The rules are checked in order.
         * The first rule that matches is used.
         */
        private List<Rule> rules;

        /*
         * Start()
         * -------
         * Called automatically by Unity when the scene starts.
         * We initialize our chatbot rules here.
         */
        private void Start()
        {
            rules = new List<Rule>
            {
                // Greeting rule
                new Rule(
                    keywords: new [] { "hello", "hi", "hey" },
                    response: "Hello! How can I help you today?"
                ),

                // Tennis related rules
                new Rule(
                    keywords: new [] { "serve" },
                    response: "For a basic serve: toss the ball slightly in front, reach up, and snap your wrist through contact."
                ),

                new Rule(
                    keywords: new [] { "forehand" },
                    response: "For a forehand: turn your shoulders, swing low to high, and follow through across your body."
                ),

                new Rule(
                    keywords: new [] { "backhand" },
                    response: "For a backhand: prepare early, keep your non dominant hand guiding, and finish forward."
                ),

                new Rule(
                    keywords: new [] { "score", "scoring" },
                    response: "Tennis scoring goes: 15, 30, 40, game. At 40 40 it is deuce."
                ),

                // Help rule
                new Rule(
                    keywords: new [] { "help", "what can you do", "commands" },
                    response: "Try typing: hello, serve, forehand, backhand, score."
                )
            };

            // Initial bot message when the chat starts
            AddMessage("Bot: Ready. Type a message and press Enter.", MessageType.Bot);
        }

        /*
         * AddMessage()
         * ------------
         * Adds a message to the chat panel and keeps only
         * the most recent 25 messages.
         */
        public void AddMessage(string messageText, MessageType messageType)
        {
            // Limit total messages to avoid clutter
            if (Messages.Count >= 25)
            {
                Destroy(Messages[0].TextObject.gameObject);
                Messages.RemoveAt(0);
            }

            // Create a new message data object
            var newMessage = new Message
            {
                Text = messageText,
                MessageType = messageType
            };

            // Instantiate the UI text prefab
            var newText = Instantiate(textObject, chatPanel.transform);
            newMessage.TextObject = newText.GetComponent<Text>();

            // Set displayed text and color
            newMessage.TextObject.text = messageText;
            newMessage.TextObject.color =
                messageType == MessageType.User ? UserColor : BotColor;

            // Store message
            Messages.Add(newMessage);
        }

        /*
         * SendMessageToBot()
         * ------------------
         * Called when the user presses Enter.
         * Sends the user's message to the chatbot
         * and displays the bot's response.
         */
        public void SendMessageToBot()
        {
            var userMessage = chatBox.text;

            // Ignore empty input
            if (string.IsNullOrWhiteSpace(userMessage))
                return;

            // Display user message
            AddMessage($"User: {userMessage}", MessageType.User);

            // Get bot response using rule matching
            string botReply = GetBotResponse(userMessage);

            // Display bot response
            AddMessage($"Bot: {botReply}", MessageType.Bot);

            // Reset input field
            chatBox.Select();
            chatBox.text = "";
        }

        /*
         * GetBotResponse()
         * ----------------
         * Core chatbot logic:
         * - Normalize the user message
         * - Check each rule in order
         * - Return the first matching response
         */
        private string GetBotResponse(string userMessage)
        {
            // Convert to lowercase for case insensitive matching
            string msg = userMessage.Trim().ToLowerInvariant();

            // Check rules one by one
            foreach (var rule in rules)
            {
                if (rule.Matches(msg))
                    return rule.Response;
            }

            // Fallback if nothing matches
            return "I did not understand that. Try typing: help.";
        }

        /*
         * Update()
         * --------
         * Called once per frame by Unity.
         * We check for the Enter key to send messages.
         */
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                SendMessageToBot();
            }
        }

        /*
         * Rule
         * ----
         * Represents a single keyword matching rule.
         */
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

            /*
             * Matches()
             * ---------
             * Returns true if ANY keyword appears in the user message.
             */
            public bool Matches(string normalizedMessage)
            {
                foreach (var keyword in Keywords)
                {
                    if (normalizedMessage.Contains(keyword.ToLowerInvariant()))
                        return true;
                }
                return false;
            }
        }
    }
}
