import { Ionicons } from "@expo/vector-icons";
import { useFocusEffect, useRouter } from "expo-router";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  FlatList,
  Keyboard,
  KeyboardAvoidingView,
  Modal,
  NativeScrollEvent,
  NativeSyntheticEvent,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { GlassCard } from "../../../src/components/ui/GlassCard";
import { IconButton } from "../../../src/components/ui/IconButton";
import { PrimaryButton } from "../../../src/components/ui/PrimaryButton";
import { ScreenContainer } from "../../../src/components/ui/ScreenContainer";
import {
  type CompanionChat,
  type CompanionMessage,
  getCompanionChats,
  setCompanionChats
} from "../../../src/features/planner/chatHistory";
import { getFloatingTabBarContentInset } from "../../../src/theme/insets";
import { palette, spacing, typography } from "../../../src/theme/tokens";

type PromptSeed = {
  text: string;
  keywords: string[];
};

type IntroPromptPair = {
  intro: string;
  placeholder: string;
};

const introPromptPairs: IntroPromptPair[] = [
  {
    intro: "Need help making a financial decision?",
    placeholder: "Make smart moves..."
  },
  {
    intro: "What should we tackle first today?",
    placeholder: "Pick the best next step..."
  },
  {
    intro: "Want to sort your goals this week?",
    placeholder: "Build a focused plan..."
  },
  {
    intro: "Thinking about food, travel, or money?",
    placeholder: "Let's explore our options..."
  },
  {
    intro: "Not sure where your budget is slipping?",
    placeholder: "Find spending pressure..."
  },
  {
    intro: "Need a calm plan for this month?",
    placeholder: "Set a clear strategy..."
  },
  {
    intro: "Trying to decide what you can afford?",
    placeholder: "Check affordability now..."
  },
  {
    intro: "Want a smarter weekend money plan?",
    placeholder: "Shape your weekend budget..."
  },
  {
    intro: "Curious what changed from last month?",
    placeholder: "Compare trends clearly..."
  },
  {
    intro: "Ready to tighten spending without stress?",
    placeholder: "Simplify your next choice..."
  }
];

const promptLibrary: PromptSeed[] = [
  { text: "How am I doing this month overall?", keywords: ["month", "overall", "doing", "progress"] },
  { text: "What changed from last month?", keywords: ["changed", "last month", "difference"] },
  { text: "Where can I save this week?", keywords: ["save", "saving", "week", "reduce"] },
  { text: "Show me food spending ideas to cut costs.", keywords: ["food", "eat", "restaurant", "groceries", "takeout"] },
  { text: "Find a realistic grocery budget target.", keywords: ["food", "groceries", "budget", "save"] },
  { text: "How much am I spending on restaurants and takeout?", keywords: ["eat", "restaurant", "takeout", "dining"] },
  { text: "Give me low-cost dinner plan ideas for this week.", keywords: ["food", "dinner", "plan", "week"] },
  { text: "How much do drinks and coffee runs cost me monthly?", keywords: ["drink", "drinks", "coffee", "cafe", "pub"] },
  { text: "Suggest a coffee budget that still feels realistic.", keywords: ["coffee", "cafe", "save", "budget"] },
  { text: "Any low-cost pubs or cafes I should try nearby?", keywords: ["drink", "pub", "cafe", "nearby"] },
  { text: "What affordable activities can I do nearby this weekend?", keywords: ["nearby", "activities", "weekend", "ideas"] },
  { text: "Can we plan today around a spending limit?", keywords: ["day", "today", "limit", "plan"] },
  { text: "Build a simple day budget for me.", keywords: ["day", "budget", "plan"] },
  { text: "I'm worried about money this month. What should I prioritize?", keywords: ["worry", "money", "priority", "stress"] },
  { text: "Help me build a small emergency savings target.", keywords: ["emergency", "save", "savings", "target"] },
  { text: "How can I reduce my subscription spending?", keywords: ["subscription", "subscriptions", "reduce", "save"] },
  { text: "Show me a debt-first payoff focus for this month.", keywords: ["debt", "payoff", "month", "priority"] },
  { text: "What rent and housing costs should I watch this month?", keywords: ["rent", "housing", "mortgage", "home"] },
  { text: "Can I afford a vacation this season?", keywords: ["vacation", "travel", "trip", "season"] },
  { text: "Plan a low-cost vacation budget outline.", keywords: ["vacation", "travel", "budget", "afford"] },
  { text: "How should I split spending for a travel month?", keywords: ["travel", "split", "month", "budget"] },
  { text: "What would a low-cost flights and hotels plan look like?", keywords: ["flight", "flights", "hotel", "travel", "vacation"] },
  { text: "Can I fit a beach trip into my current budget?", keywords: ["beach", "trip", "travel", "afford"] },
  { text: "Should I delay my trip or is it still affordable?", keywords: ["trip", "vacation", "affordable", "budget"] },
  { text: "How can I handle mortgage pressure better?", keywords: ["mortgage", "housing", "affordability"] },
  { text: "Are there grants or supports I should look into?", keywords: ["grant", "grants", "support", "mortgage"] },
  { text: "Help me compare transport costs this month.", keywords: ["transport", "commute", "car", "fuel"] },
  { text: "What shopping habits are hurting my budget?", keywords: ["shopping", "spend", "habits", "budget"] },
  { text: "How can I shop smarter without feeling restricted?", keywords: ["shopping", "save", "budget", "smart"] },
  { text: "How much is weekend fun usually costing me?", keywords: ["weekend", "fun", "activity", "spending"] },
  { text: "Help me plan a date night within budget.", keywords: ["date", "night", "budget", "fun"] },
  { text: "Any affordable social ideas for this weekend?", keywords: ["social", "ideas", "weekend", "affordable"] },
  { text: "How much am I spending on entertainment lately?", keywords: ["entertainment", "fun", "spending"] },
  { text: "Suggest healthy spending choices for social plans.", keywords: ["social", "plans", "healthy", "spending"] },
  { text: "Can we build a savings goal for the next 3 months?", keywords: ["save", "savings", "goal", "months"] },
  { text: "How much should I save from my next salary?", keywords: ["salary", "income", "save", "goal"] },
  { text: "What income changes happened month over month?", keywords: ["income", "month", "compare", "salary"] },
  { text: "Can I afford this purchase right now?", keywords: ["afford", "purchase", "budget", "shopping"] },
  { text: "Where are my biggest budget leaks right now?", keywords: ["budget", "leaks", "overspend", "save"] },
  { text: "How much should I leave for necessities before weekend spending?", keywords: ["necessities", "weekend", "spending", "plan"] },
  { text: "How do I plan food, transport, and social spending this week?", keywords: ["food", "transport", "social", "week"] },
  { text: "What should I watch this month before payday?", keywords: ["payday", "month", "watch", "cashflow"] },
  { text: "What is the smartest next money move today?", keywords: ["next", "move", "today", "plan"] },
  { text: "How should I split utilities, rent, and food this month?", keywords: ["utilities", "rent", "food", "split", "month"] },
  { text: "Could I handle a grant application with my current finances?", keywords: ["grant", "application", "housing", "mortgage"] },
  { text: "Can you suggest a simple no-stress budget reset?", keywords: ["budget", "reset", "stress", "simple"] }
];

const createChat = (): CompanionChat => {
  const now = new Date().toISOString();
  return {
    id: `chat-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
    title: "New conversation",
    createdUtc: now,
    updatedUtc: now,
    messages: []
  };
};

const formatChatTitle = (chat: CompanionChat) => {
  if (chat.messages.length === 0) {
    return "New conversation";
  }

  const firstUserMessage = chat.messages.find((item) => item.role === "user")?.text;
  if (!firstUserMessage) {
    return "Planner conversation";
  }

  return firstUserMessage.length > 36
    ? `${firstUserMessage.slice(0, 36).trim()}...`
    : firstUserMessage;
};

function pickPromptPair(lastIntro?: string): IntroPromptPair {
  const filtered = introPromptPairs.filter((item) => item.intro !== lastIntro);
  const nextPool = filtered.length > 0 ? filtered : introPromptPairs;
  return nextPool[Math.floor(Math.random() * nextPool.length)];
}

function rotateStarterPrompts(previous: string[] = []): string[] {
  const source = promptLibrary.map((item) => item.text);
  for (let attempt = 0; attempt < 5; attempt += 1) {
    const next = [...source].sort(() => Math.random() - 0.5).slice(0, 10);
    if (previous.length === 0 || next.join("|") !== previous.join("|")) {
      return next;
    }
  }

  return [...source].sort(() => Math.random() - 0.5).slice(0, 10);
}

function rankPrompts(input: string) {
  const normalized = input.trim().toLowerCase();
  const tokens = normalized.split(/\s+/).filter(Boolean);
  const aliasMap: Record<string, string[]> = {
    eat: ["food", "dinner", "lunch", "breakfast", "groceries", "restaurant", "takeout"],
    food: ["dinner", "lunch", "breakfast", "groceries", "restaurant", "takeout"],
    drink: ["coffee", "pub", "bar", "cafe", "drinks"],
    coffee: ["drink", "cafe", "budget", "save"],
    trip: ["travel", "vacation", "flight", "hotel", "beach"],
    vacation: ["travel", "flight", "hotel", "trip", "beach"],
    mortgage: ["house", "housing", "rent", "grant", "affordability"],
    rent: ["housing", "house", "mortgage", "grant"],
    save: ["savings", "budget", "reduce", "cut", "goal"],
    budget: ["save", "savings", "reduce", "cut back", "afford"],
    fun: ["activity", "weekend", "date", "social", "entertainment"],
    weekend: ["activity", "fun", "social", "date", "plan"],
    loan: ["debt", "credit", "payoff"],
    debt: ["loan", "credit", "payoff"],
    beach: ["trip", "travel", "vacation", "hotel", "flight"],
    grant: ["mortgage", "housing", "support"],
    shop: ["shopping", "budget", "save"],
    subscription: ["subscriptions", "monthly", "cost", "reduce"],
    afford: ["budget", "save", "vacation", "mortgage", "rent"]
  };
  const expandedTerms = new Set(tokens);
  tokens.forEach((token) => {
    (aliasMap[token] ?? []).forEach((alias) => expandedTerms.add(alias));
  });

  const scored = promptLibrary.map((prompt, index) => {
    if (!normalized) {
      return {
        prompt,
        score: 1,
        index
      };
    }

    let score = 0;
    prompt.keywords.forEach((keyword) => {
      if (normalized.includes(keyword)) {
        score += 8;
        return;
      }

      if (
        [...expandedTerms].some(
          (term) => keyword.startsWith(term) || term.startsWith(keyword)
        )
      ) {
        score += 4;
      }
    });

    if (normalized.length >= 3 && prompt.text.toLowerCase().includes(normalized)) {
      score += 5;
    }

    return {
      prompt,
      score,
      index
    };
  });

  const filtered = normalized ? scored.filter((item) => item.score > 0) : scored;
  const sorted = filtered.sort((left, right) => {
    if (right.score !== left.score) {
      return right.score - left.score;
    }

    return left.index - right.index;
  });

  return sorted.slice(0, 10).map((item) => item.prompt.text);
}

export default function PlannerCompanionScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const showEvent = Platform.OS === "ios" ? "keyboardWillShow" : "keyboardDidShow";
  const hideEvent = Platform.OS === "ios" ? "keyboardWillHide" : "keyboardDidHide";
  const [isReady, setIsReady] = useState(false);
  const [chats, setChats] = useState<CompanionChat[]>([]);
  const [activeChatId, setActiveChatId] = useState<string>("");
  const [input, setInput] = useState("");
  const [isInputFocused, setIsInputFocused] = useState(false);
  const [isKeyboardVisible, setIsKeyboardVisible] = useState(false);
  const [historyVisible, setHistoryVisible] = useState(false);
  const [introPair, setIntroPair] = useState<IntroPromptPair>(() => pickPromptPair());
  const [defaultPromptSet, setDefaultPromptSet] = useState<string[]>(() =>
    rotateStarterPrompts()
  );
  const [inputBarHeight, setInputBarHeight] = useState(52);
  const [promptLayerHeight, setPromptLayerHeight] = useState(0);
  const lastIntroRef = useRef(introPair.intro);
  const messageListRef = useRef<FlatList<CompanionMessage>>(null);
  const promptScrollRef = useRef<ScrollView>(null);
  const promptViewportWidthRef = useRef(0);
  const promptContentWidthRef = useRef(0);
  const promptOffsetRef = useRef(0);
  const promptAutoScrollIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const promptAutoScrollResumeTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const chatScrollRetryTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const chatScrollTimersRef = useRef<ReturnType<typeof setTimeout>[]>([]);
  const shouldForceBottomRef = useRef(false);
  const hadMeaningfulInputRef = useRef(false);
  const chatContentHeightRef = useRef(0);
  const chatViewportHeightRef = useRef(0);

  useEffect(() => {
    const load = async () => {
      const stored = await getCompanionChats();
      if (stored.length === 0) {
        const initial = createChat();
        setChats([initial]);
        setActiveChatId(initial.id);
        setIsReady(true);
        return;
      }

      const ordered = [...stored].sort(
        (a, b) => new Date(b.updatedUtc).getTime() - new Date(a.updatedUtc).getTime()
      );
      setChats(ordered);
      setActiveChatId(ordered[0].id);
      setIsReady(true);
    };

    void load();
  }, []);

  useEffect(() => {
    if (!isReady) {
      return;
    }

    void setCompanionChats(chats);
  }, [chats, isReady]);

  useEffect(() => {
    const nextPair = pickPromptPair(lastIntroRef.current);
    lastIntroRef.current = nextPair.intro;
    setIntroPair(nextPair);
  }, [activeChatId]);

  useFocusEffect(
    useCallback(() => {
      setDefaultPromptSet((current) => rotateStarterPrompts(current));
      const nextPair = pickPromptPair(lastIntroRef.current);
      lastIntroRef.current = nextPair.intro;
      setIntroPair(nextPair);
      const focusedChat = chats.find((item) => item.id === activeChatId) ?? chats[0];
      if (isReady && (focusedChat?.messages.length ?? 0) > 0) {
        shouldForceBottomRef.current = true;
      }
      return undefined;
    }, [activeChatId, chats, isReady])
  );

  useEffect(() => {
    const showSubscription = Keyboard.addListener(showEvent, () => setIsKeyboardVisible(true));
    const hideSubscription = Keyboard.addListener(hideEvent, () => setIsKeyboardVisible(false));
    return () => {
      showSubscription.remove();
      hideSubscription.remove();
    };
  }, [hideEvent, showEvent]);

  const activeChat = useMemo(
    () => chats.find((item) => item.id === activeChatId) ?? chats[0],
    [activeChatId, chats]
  );

  const clearChatScrollTimers = useCallback(() => {
    chatScrollTimersRef.current.forEach((timer) => clearTimeout(timer));
    chatScrollTimersRef.current = [];
    if (chatScrollRetryTimerRef.current) {
      clearTimeout(chatScrollRetryTimerRef.current);
      chatScrollRetryTimerRef.current = null;
    }
  }, []);

  const forceScrollToAbsoluteBottom = useCallback((animated = false) => {
    try {
      const maxOffset = Math.max(
        0,
        chatContentHeightRef.current - chatViewportHeightRef.current
      );
      messageListRef.current?.scrollToOffset({
        offset: maxOffset,
        animated
      });
    } catch {
      // Ignore transient list-layout timing issues; retries handle this.
    }
  }, []);

  const scheduleForceBottomSnap = useCallback(() => {
    clearChatScrollTimers();
    const passes = [0, 60, 150, 280, 460];
    passes.forEach((delay) => {
      const timer = setTimeout(() => {
        forceScrollToAbsoluteBottom(false);
      }, delay);
      chatScrollTimersRef.current.push(timer);
    });
  }, [clearChatScrollTimers, forceScrollToAbsoluteBottom]);

  const scrollChatToBottom = useCallback((animated = true) => {
    clearChatScrollTimers();

    requestAnimationFrame(() => {
      forceScrollToAbsoluteBottom(animated);
    });

    // Layout can still settle a moment later when page opens from cached chats.
    chatScrollRetryTimerRef.current = setTimeout(() => {
      forceScrollToAbsoluteBottom(false);
    }, 120);
  }, [clearChatScrollTimers, forceScrollToAbsoluteBottom]);

  const upsertMessages = useCallback((messages: CompanionMessage[]) => {
    if (!activeChat) {
      return;
    }

    const nextUpdatedUtc = new Date().toISOString();
    setChats((current) => {
      const updated = current.map((chat) =>
        chat.id === activeChat.id
          ? {
              ...chat,
              messages,
              title: formatChatTitle({ ...chat, messages }),
              updatedUtc: nextUpdatedUtc
            }
          : chat
      );

      return [...updated].sort(
        (left, right) => new Date(right.updatedUtc).getTime() - new Date(left.updatedUtc).getTime()
      );
    });
  }, [activeChat]);

  const sendPrompt = useCallback((prompt: string) => {
    if (!prompt.trim()) {
      return;
    }

    const now = new Date().toISOString();
    const userMessage: CompanionMessage = {
      id: `${Date.now()}-u`,
      role: "user",
      text: prompt,
      createdUtc: now
    };

    const assistantMessage: CompanionMessage = {
      id: `${Date.now()}-a`,
      role: "assistant",
      text: "I can help with guidance using your current planning context. Deeper intelligence will improve as your transaction context and necessities coverage grow.",
      createdUtc: now
    };

    const currentMessages = activeChat?.messages ?? [];
    upsertMessages([...currentMessages, userMessage, assistantMessage]);
    setInput("");
  }, [activeChat, upsertMessages]);

  const startNewChat = () => {
    const nextChat = createChat();
    setChats((current) => [nextChat, ...current]);
    setActiveChatId(nextChat.id);
    setInput("");
    setHistoryVisible(false);
    setDefaultPromptSet((current) => rotateStarterPrompts(current));
  };

  const showPrompts = isInputFocused || input.trim().length > 0;
  const promptSuggestions = useMemo(() => {
    if (!showPrompts) {
      return [];
    }

    if (!input.trim()) {
      return defaultPromptSet;
    }

    return rankPrompts(input);
  }, [defaultPromptSet, input, showPrompts]);
  const closedInputBottomInset = Math.max(
    spacing[8],
    getFloatingTabBarContentInset(insets.bottom, spacing[8])
  );
  const inputBottomInset = isKeyboardVisible ? spacing[20] : closedInputBottomInset;
  const chatBottomPadding =
    inputBottomInset +
    inputBarHeight +
    spacing[12] +
    (showPrompts ? promptLayerHeight + spacing[8] : spacing[4]);

  useEffect(() => {
    const hasMeaningfulInput = input.trim().length > 0;
    if (hadMeaningfulInputRef.current && !hasMeaningfulInput) {
      setDefaultPromptSet((current) => rotateStarterPrompts(current));
    }

    hadMeaningfulInputRef.current = hasMeaningfulInput;
  }, [input]);

  const stopPromptAutoScroll = useCallback(() => {
    if (promptAutoScrollIntervalRef.current) {
      clearInterval(promptAutoScrollIntervalRef.current);
      promptAutoScrollIntervalRef.current = null;
    }

    if (promptAutoScrollResumeTimerRef.current) {
      clearTimeout(promptAutoScrollResumeTimerRef.current);
      promptAutoScrollResumeTimerRef.current = null;
    }
  }, []);

  const startPromptAutoScroll = useCallback(() => {
    stopPromptAutoScroll();

    if (!showPrompts) {
      return;
    }

    const maxOffset = Math.max(
      0,
      promptContentWidthRef.current - promptViewportWidthRef.current
    );

    if (maxOffset <= 8) {
      return;
    }

    promptAutoScrollIntervalRef.current = setInterval(() => {
      const latestMaxOffset = Math.max(
        0,
        promptContentWidthRef.current - promptViewportWidthRef.current
      );
      if (latestMaxOffset <= 8) {
        return;
      }

      const nextOffset = promptOffsetRef.current + 0.9;
      if (nextOffset >= latestMaxOffset) {
        promptOffsetRef.current = 0;
        promptScrollRef.current?.scrollTo({ x: 0, animated: false });
        return;
      }

      promptOffsetRef.current = nextOffset;
      promptScrollRef.current?.scrollTo({ x: nextOffset, animated: false });
    }, 42);
  }, [showPrompts, stopPromptAutoScroll]);

  const schedulePromptAutoScroll = useCallback(
    (delayMs = 3000) => {
      stopPromptAutoScroll();
      if (!showPrompts) {
        return;
      }

      promptAutoScrollResumeTimerRef.current = setTimeout(() => {
        startPromptAutoScroll();
      }, delayMs);
    },
    [showPrompts, startPromptAutoScroll, stopPromptAutoScroll]
  );

  const handlePromptScroll = (event: NativeSyntheticEvent<NativeScrollEvent>) => {
    promptOffsetRef.current = event.nativeEvent.contentOffset.x;
  };

  useEffect(() => {
    if (!activeChat?.messages.length) {
      return;
    }

    shouldForceBottomRef.current = true;
    scrollChatToBottom(true);
  }, [activeChat?.messages.length, scrollChatToBottom]);

  useEffect(() => {
    if ((activeChat?.messages.length ?? 0) > 0) {
      shouldForceBottomRef.current = true;
    }
  }, [activeChatId, activeChat?.messages.length]);

  useEffect(
    () => () => {
      clearChatScrollTimers();
    },
    [clearChatScrollTimers]
  );

  useEffect(() => {
    if (!showPrompts) {
      stopPromptAutoScroll();
      return;
    }

    promptOffsetRef.current = 0;
    promptScrollRef.current?.scrollTo({ x: 0, animated: false });
    schedulePromptAutoScroll();
    return () => {
      stopPromptAutoScroll();
    };
  }, [promptSuggestions, schedulePromptAutoScroll, showPrompts, stopPromptAutoScroll]);

  return (
    <ScreenContainer
      scrollable={false}
      contentStyle={styles.content}
    >
      <KeyboardAvoidingView
        style={styles.keyboardWrap}
        behavior={Platform.OS === "ios" ? "padding" : "height"}
        keyboardVerticalOffset={
          Platform.OS === "ios"
            ? 8 + Math.max(insets.bottom, 0)
            : 16 + Math.max(insets.bottom, 0)
        }
      >
        <View style={styles.headerRow}>
          <IconButton
            onPress={() => router.back()}
            icon={<Ionicons name="arrow-back" size={18} color={palette.textPrimary} />}
          />
          <Text style={styles.headerTitle}>NS Companion</Text>
          <IconButton
            onPress={() => setHistoryVisible(true)}
            icon={<Ionicons name="menu-outline" size={18} color={palette.textPrimary} />}
          />
        </View>

        <View style={styles.chatWrap}>
          {activeChat?.messages.length ? (
            <FlatList
              ref={messageListRef}
              data={activeChat.messages}
              keyExtractor={(item) => item.id}
              contentContainerStyle={[styles.chatList, { paddingBottom: chatBottomPadding }]}
              showsVerticalScrollIndicator={false}
              keyboardShouldPersistTaps="handled"
              onLayout={(event) => {
                chatViewportHeightRef.current = event.nativeEvent.layout.height;
                if (shouldForceBottomRef.current) {
                  scheduleForceBottomSnap();
                }
              }}
              onContentSizeChange={(_width, height) => {
                chatContentHeightRef.current = height;
                if (!shouldForceBottomRef.current) {
                  return;
                }

                scheduleForceBottomSnap();
                const settleTimer = setTimeout(() => {
                  shouldForceBottomRef.current = false;
                }, 520);
                chatScrollTimersRef.current.push(settleTimer);
              }}
              renderItem={({ item }) => (
                <GlassCard
                  style={[
                    styles.chatBubble,
                    item.role === "assistant" ? styles.assistantBubble : styles.userBubble
                  ]}
                >
                  <Text style={styles.chatRole}>{item.role === "assistant" ? "Companion" : "You"}</Text>
                  <Text style={styles.chatText}>{item.text}</Text>
                </GlassCard>
              )}
            />
          ) : (
            <View style={styles.emptyWrap}>
              <Text style={styles.emptyIntro}>{introPair.intro}</Text>
            </View>
          )}
        </View>

        <View style={[styles.inputArea, { bottom: inputBottomInset }]} pointerEvents="box-none">
          {showPrompts ? (
            <View
              style={styles.promptLayer}
              onLayout={(event) => {
                setPromptLayerHeight(event.nativeEvent.layout.height);
              }}
            >
              <ScrollView
                ref={promptScrollRef}
                horizontal
                showsHorizontalScrollIndicator={false}
                contentContainerStyle={styles.promptCarousel}
                keyboardShouldPersistTaps="handled"
                onLayout={(event) => {
                  promptViewportWidthRef.current = event.nativeEvent.layout.width;
                  schedulePromptAutoScroll();
                }}
                onContentSizeChange={(width) => {
                  promptContentWidthRef.current = width;
                  schedulePromptAutoScroll();
                }}
                onScroll={handlePromptScroll}
                scrollEventThrottle={16}
                onScrollBeginDrag={() => stopPromptAutoScroll()}
                onScrollEndDrag={() => schedulePromptAutoScroll()}
                onMomentumScrollEnd={() => schedulePromptAutoScroll()}
              >
                {promptSuggestions.map((prompt) => (
                  <Pressable
                    key={prompt}
                    style={({ pressed }) => [
                      styles.promptChip,
                      pressed ? styles.promptChipPressed : null
                    ]}
                    onPress={() => sendPrompt(prompt)}
                  >
                    <Text style={styles.promptChipText}>{prompt}</Text>
                  </Pressable>
                ))}
              </ScrollView>
            </View>
          ) : null}

          <View
            style={styles.inputBar}
            onLayout={(event) => {
              setInputBarHeight(event.nativeEvent.layout.height);
            }}
          >
            <TextInput
              value={input}
              onChangeText={setInput}
              onFocus={() => setIsInputFocused(true)}
              onBlur={() => setIsInputFocused(false)}
              placeholder={introPair.placeholder}
              placeholderTextColor={palette.textSecondary}
              style={styles.input}
              multiline
              maxLength={280}
            />
            <Pressable
              style={({ pressed }) => [styles.sendButton, pressed ? styles.sendPressed : null]}
              onPress={() => sendPrompt(input.trim())}
            >
              <Ionicons name="arrow-up" size={16} color={palette.appBackground} />
            </Pressable>
          </View>
        </View>
      </KeyboardAvoidingView>

      <Modal visible={historyVisible} transparent animationType="fade" onRequestClose={() => setHistoryVisible(false)}>
        <Pressable style={styles.historyOverlay} onPress={() => setHistoryVisible(false)}>
          <Pressable style={styles.historySheet} onPress={() => undefined}>
            <View style={styles.historyHeader}>
              <Text style={styles.historyTitle}>Chats</Text>
              <IconButton
                onPress={() => setHistoryVisible(false)}
                icon={<Ionicons name="close" size={16} color={palette.textPrimary} />}
              />
            </View>

            <PrimaryButton label="New chat" onPress={startNewChat} icon={<Ionicons name="add" size={16} color={palette.textPrimary} />} />

            <ScrollView contentContainerStyle={styles.historyList} showsVerticalScrollIndicator={false}>
              {chats.map((chat) => (
                <Pressable
                  key={chat.id}
                  onPress={() => {
                    setActiveChatId(chat.id);
                    setHistoryVisible(false);
                  }}
                  style={({ pressed }) => [
                    styles.historyItem,
                    chat.id === activeChat?.id ? styles.historyItemActive : null,
                    pressed ? styles.historyItemPressed : null
                  ]}
                >
                  <Text style={styles.historyItemTitle}>{chat.title}</Text>
                  <Text style={styles.historyItemMeta}>{new Date(chat.updatedUtc).toLocaleString("en-IE")}</Text>
                </Pressable>
              ))}
            </ScrollView>
          </Pressable>
        </Pressable>
      </Modal>
    </ScreenContainer>
  );
}

const styles = StyleSheet.create({
  content: {
    paddingTop: spacing[20],
    paddingBottom: 0
  },
  keyboardWrap: {
    flex: 1
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  headerTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  chatWrap: {
    flex: 1,
    marginTop: spacing[12]
  },
  chatList: {
    gap: spacing[12],
    paddingTop: spacing[12],
    flexGrow: 1,
    justifyContent: "flex-end"
  },
  chatBubble: {
    gap: spacing[8]
  },
  userBubble: {
    borderColor: "rgba(47,107,255,0.46)"
  },
  assistantBubble: {
    borderColor: "rgba(111,215,255,0.34)"
  },
  chatRole: {
    color: palette.accent,
    ...typography.caption
  },
  chatText: {
    color: palette.textPrimary,
    ...typography.body1
  },
  emptyWrap: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[20]
  },
  emptyIntro: {
    color: palette.textPrimary,
    ...typography.title2,
    fontWeight: "700",
    textAlign: "center"
  },
  inputArea: {
    position: "absolute",
    left: 0,
    right: 0,
    gap: spacing[8]
  },
  promptLayer: {
    zIndex: 3
  },
  promptCarousel: {
    gap: spacing[8],
    paddingBottom: spacing[4],
    paddingHorizontal: 2
  },
  promptChip: {
    borderRadius: 12,
    borderWidth: 1,
    borderColor: "rgba(220,232,255,0.2)",
    backgroundColor: "rgba(18,44,74,0.95)",
    justifyContent: "center",
    paddingHorizontal: 11,
    paddingVertical: 5
  },
  promptChipPressed: {
    opacity: 0.9,
    transform: [{ scale: 0.99 }]
  },
  promptChipText: {
    color: palette.textSecondary,
    ...typography.caption,
    lineHeight: 15
  },
  inputBar: {
    borderRadius: 16,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(14,30,50,0.9)",
    flexDirection: "row",
    alignItems: "flex-end",
    paddingLeft: spacing[12],
    paddingRight: spacing[8],
    paddingVertical: spacing[8],
    minHeight: 50,
    gap: spacing[8]
  },
  input: {
    flex: 1,
    color: palette.textPrimary,
    ...typography.body1,
    maxHeight: 120,
    paddingBottom: 6
  },
  sendButton: {
    width: 34,
    height: 34,
    borderRadius: 10,
    backgroundColor: palette.primaryGlow,
    alignItems: "center",
    justifyContent: "center",
    marginBottom: 2
  },
  sendPressed: {
    opacity: 0.82,
    transform: [{ scale: 0.96 }]
  },
  historyOverlay: {
    flex: 1,
    backgroundColor: "rgba(4,11,23,0.74)",
    justifyContent: "flex-end"
  },
  historySheet: {
    borderTopLeftRadius: 20,
    borderTopRightRadius: 20,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(12,25,43,0.99)",
    padding: spacing[16],
    gap: spacing[12],
    maxHeight: "72%"
  },
  historyHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  historyTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  historyList: {
    gap: spacing[8],
    paddingBottom: spacing[8]
  },
  historyItem: {
    borderRadius: 14,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.8)",
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[12],
    gap: spacing[4]
  },
  historyItemActive: {
    borderColor: palette.primaryGlow
  },
  historyItemPressed: {
    opacity: 0.9
  },
  historyItemTitle: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  historyItemMeta: {
    color: palette.textSecondary,
    ...typography.caption
  }
});
