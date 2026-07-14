import { Ionicons, MaterialCommunityIcons } from "@expo/vector-icons";
import { useFocusEffect, useRouter } from "expo-router";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  ActivityIndicator, Animated, Alert, Dimensions, Easing, FlatList, Keyboard, KeyboardAvoidingView, type KeyboardEvent, NativeScrollEvent, NativeSyntheticEvent, Platform, Pressable, ScrollView, Text, TextInput, View } from "react-native";
import { FloatingBottomNav } from "../components/layout/FloatingBottomNav";
import { appBottomNavItems } from "../components/layout/bottomNavConfigs";
import { GlassCard } from "../components/ui/GlassCard";
import { HeaderActionButton, HeaderShell } from "../layout/appHeader";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { IconButton } from "../components/ui/IconButton";
import { PrimaryButton } from "../components/ui/PrimaryButton";
import { ScreenContainer } from "../components/ui/ScreenContainer";
import { SystemModal } from "../components/ui/surfaces/SystemModal";
import {
  type CompanionChat,
  type CompanionChatColor,
  type CompanionMessage,
  deleteCompanionChat,
  getCompanionChats,
  loadCompanionChatMessages,
  setCompanionChats
} from "../features/planner/chatHistory";
import { navigateWithProbe } from "../lib/perf/navigationTiming";
import { getDockAwareContentBottomInset } from "../layout/contentFrame";
import { getEffectiveBottomSystemInset } from "../theme/insets";
import { controls, layout, navigation, palette, radius, sizing, spacing, surfaces, typography, createRuntimeStyleSheet, useThemeTokens } from "../theme/tokens";
import { archiveAIChatThread, sendAIChatMessage } from "../features/ai/aiChatApi";
import { PlaceCardCarousel } from "../features/ai/components/PlaceCardCarousel";
import {
  buildNearbyGroundingDiagnosticsMetadata,
  buildChatLocationMetadata,
  buildChatLocationState,
  type ChatLocationContext,
  normalizeTypedArea,
  resolveChatLocationAttachment
} from "../features/ai/location/chatLocationGrounding";
import { LocationPermissionPromptModal } from "../features/ai/location/LocationPermissionPromptModal";
import { LocationTypedAreaModal } from "../features/ai/location/LocationTypedAreaModal";
import {
  getForegroundLocationPermissionState,
  getFreshForegroundLocationSnapshot,
  openLocationSettings,
  requestForegroundLocationAccess
} from "../features/ai/location/locationPermissionService";
import { formatUnknownError } from "../lib/api/errors";
import { useAuthSession } from "../providers/AuthProvider";

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

type ChatColorTheme = {
  label: string;
  borderColor: string;
  backgroundColor: string;
  swatchColor: string;
};

const createChatColorThemes = (defaultSurface: string): Record<CompanionChatColor, ChatColorTheme> => ({
  blue: {
    label: "Slate",
    borderColor: "rgba(154,154,154,0.42)",
    backgroundColor: defaultSurface,
    swatchColor: "#9A9A9A"
  },
  yellow: {
    label: "Yellow",
    borderColor: "rgba(240,180,76,0.56)",
    backgroundColor: "rgba(63,50,18,0.78)",
    swatchColor: "#F0B44C"
  },
  green: {
    label: "Green",
    borderColor: "rgba(80,214,146,0.56)",
    backgroundColor: "rgba(16,56,42,0.78)",
    swatchColor: "#4DD690"
  },
  pink: {
    label: "Rose",
    borderColor: "rgba(226,90,90,0.56)",
    backgroundColor: "rgba(70,34,34,0.78)",
    swatchColor: "#E25A5A"
  },
  red: {
    label: "Red",
    borderColor: "rgba(226,90,90,0.56)",
    backgroundColor: "rgba(82,22,35,0.76)",
    swatchColor: "#E25A5A"
  },
  white: {
    label: "Silver",
    borderColor: "rgba(216,216,216,0.7)",
    backgroundColor: "rgba(58,58,58,0.62)",
    swatchColor: "#D8D8D8"
  },
  orange: {
    label: "Orange",
    borderColor: "rgba(242,140,40,0.56)",
    backgroundColor: "rgba(88,45,15,0.78)",
    swatchColor: "#F28C28"
  },
  purple: {
    label: "Charcoal",
    borderColor: "rgba(154,154,154,0.56)",
    backgroundColor: "rgba(42,42,42,0.78)",
    swatchColor: "#9A9A9A"
  },
  brown: {
    label: "Brown",
    borderColor: "rgba(185,138,95,0.56)",
    backgroundColor: "rgba(74,48,30,0.78)",
    swatchColor: "#B98A5E"
  }
});

const CHAT_COLOR_ORDER: CompanionChatColor[] = [
  "blue",
  "yellow",
  "green",
  "pink",
  "red",
  "white",
  "orange",
  "purple",
  "brown"
];

const CHAT_TITLE_MAX_LENGTH = 56;
const MAX_CLIENT_REQUEST_ID_LENGTH = 80;

const getChatColorTheme = (
  color: CompanionChatColor | undefined,
  themes: Record<CompanionChatColor, ChatColorTheme>
): ChatColorTheme => themes[color ?? "orange"];

const sortChatsByPinAndRecency = (items: CompanionChat[]): CompanionChat[] => {
  return [...items].sort((left, right) => {
    if (left.isPinned !== right.isPinned) {
      return left.isPinned ? -1 : 1;
    }

    if (left.isPinned && right.isPinned) {
      const leftPinned = new Date(left.pinnedUtc ?? left.updatedUtc).getTime();
      const rightPinned = new Date(right.pinnedUtc ?? right.updatedUtc).getTime();
      if (rightPinned !== leftPinned) {
        return rightPinned - leftPinned;
      }
    }

    return new Date(right.updatedUtc).getTime() - new Date(left.updatedUtc).getTime();
  });
};

const createChat = (): CompanionChat => {
  const now = new Date().toISOString();
  return {
    id: `chat-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
    title: "New conversation",
    createdUtc: now,
    updatedUtc: now,
    messages: [],
    messagesLoaded: true,
    conversationThreadId: null,
    activeResultSetId: null,
    selectedEntityId: null,
    pendingClarificationSlot: null,
    pendingClarificationPromptIntent: null,
    color: "orange",
    isPinned: false,
    pinnedUtc: null
  };
};

const createClientRequestId = (): string => {
  const timestamp = Date.now().toString(36);
  const randomPart = Math.random().toString(36).slice(2, 12);
  return `chat-${timestamp}-${randomPart}`.slice(0, MAX_CLIENT_REQUEST_ID_LENGTH);
};

const formatChatTitle = (chat: CompanionChat) => {
  if (chat.messages.length === 0) {
    return "New conversation";
  }

  const firstUserMessage = chat.messages.find((item) => item.role === "user")?.text;
  if (!firstUserMessage) {
    return "Cashflow conversation";
  }

  return firstUserMessage.length > 36
    ? `${firstUserMessage.slice(0, 36).trim()}...`
    : firstUserMessage;
};

const getStructuredPlacesIntroText = (text: string) => {
  const lines = text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
  const firstNonListLine = lines.find((line) => !/^\d+[\).]\s+/.test(line));

  return firstNonListLine || "I found these matching options:";
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

type PendingNearbyRequest = {
  chatId: string;
  prompt: string;
  diagnosticsMetadata: Record<string, string>;
};

const NEARBY_GROUNDING_FAIL_LOUD_MESSAGE =
  "We couldn’t access your location. Please enable location or type an area.";

export default function CashflowCompanionScreen() {
  const tokens = useThemeTokens();
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const { session } = useAuthSession();
  const userId = session?.user.id ?? null;
  const activeBottomKey = "__none__";
  const showEvent = Platform.OS === "ios" ? "keyboardWillShow" : "keyboardDidShow";
  const hideEvent = Platform.OS === "ios" ? "keyboardWillHide" : "keyboardDidHide";
  const [isReady, setIsReady] = useState(false);
  const [chats, setChats] = useState<CompanionChat[]>([]);
  const [activeChatId, setActiveChatId] = useState<string>("");
  const [input, setInput] = useState("");
  const [isSending, setIsSending] = useState(false);
  const [isConversationLoading, setIsConversationLoading] = useState(false);
  const [sendingChatId, setSendingChatId] = useState<string | null>(null);
  const [sendError, setSendError] = useState<string | null>(null);
  const [sendInfo, setSendInfo] = useState<string | null>(null);
  const [nearbyPermissionPromptVisible, setNearbyPermissionPromptVisible] = useState(false);
  const [locationSettingsPromptVisible, setLocationSettingsPromptVisible] = useState(false);
  const [typedAreaPromptVisible, setTypedAreaPromptVisible] = useState(false);
  const [typedAreaInput, setTypedAreaInput] = useState("");
  const [pendingNearbyRequest, setPendingNearbyRequest] = useState<PendingNearbyRequest | null>(null);
  const [locationActionInProgress, setLocationActionInProgress] = useState(false);
  const [isInputFocused, setIsInputFocused] = useState(false);
  const [isKeyboardVisible, setIsKeyboardVisible] = useState(false);
  const [keyboardOverlap, setKeyboardOverlap] = useState(0);
  const [historyVisible, setHistoryVisible] = useState(false);
  const [editChatVisible, setEditChatVisible] = useState(false);
  const [editingChatId, setEditingChatId] = useState<string | null>(null);
  const [editingChatTitle, setEditingChatTitle] = useState("");
  const [editingChatColor, setEditingChatColor] = useState<CompanionChatColor>("orange");
  const [introPair, setIntroPair] = useState<IntroPromptPair>(() => pickPromptPair());
  const [defaultPromptSet, setDefaultPromptSet] = useState<string[]>(() =>
    rotateStarterPrompts()
  );
  const [inputBarHeight, setInputBarHeight] = useState(52);
  const [promptLayerHeight, setPromptLayerHeight] = useState(0);
  const chatColorThemes = useMemo(() => createChatColorThemes(tokens.surfaces.field), [tokens.surfaces.field]);
  const inputBottomAnimated = useRef(new Animated.Value(0)).current;
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
  const failedSendRef = useRef<{
    chatId: string;
    message: string;
    clientRequestId: string;
  } | null>(null);
  const loadedHistoryUserIdRef = useRef<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    loadedHistoryUserIdRef.current = null;
    setIsReady(false);
    setChats([]);
    setActiveChatId("");
    setIsConversationLoading(false);

    const load = async () => {
      if (!userId) {
        if (!cancelled) {
          setIsReady(true);
        }
        return;
      }

      try {
        const stored = await getCompanionChats(userId);
        if (cancelled) {
          return;
        }

        if (stored.length === 0) {
          const initial = createChat();
          setChats([initial]);
          setActiveChatId(initial.id);
          loadedHistoryUserIdRef.current = userId;
          setIsReady(true);
          return;
        }

        const ordered = sortChatsByPinAndRecency(stored);
        setChats(ordered);
        setActiveChatId(ordered[0].id);
        loadedHistoryUserIdRef.current = userId;
        setIsReady(true);
      } catch (error) {
        if (cancelled) {
          return;
        }

        const initial = createChat();
        setChats([initial]);
        setActiveChatId(initial.id);
        setSendError(formatUnknownError(error));
        loadedHistoryUserIdRef.current = userId;
        setIsReady(true);
      }
    };

    void load();

    return () => {
      cancelled = true;
    };
  }, [userId]);

  useEffect(() => {
    if (!isReady || !userId || loadedHistoryUserIdRef.current !== userId) {
      return;
    }

    void setCompanionChats(userId, chats).catch(() => undefined);
  }, [chats, isReady, userId]);

  useEffect(() => {
    const nextPair = pickPromptPair(lastIntroRef.current);
    lastIntroRef.current = nextPair.intro;
    setIntroPair(nextPair);
  }, [activeChatId]);

  useEffect(() => {
    setSendError(null);
    setSendInfo(null);
  }, [activeChatId]);

  useEffect(() => {
    if (!pendingNearbyRequest) {
      return;
    }

    if (pendingNearbyRequest.chatId !== activeChatId) {
      setPendingNearbyRequest(null);
      setNearbyPermissionPromptVisible(false);
      setLocationSettingsPromptVisible(false);
      setTypedAreaPromptVisible(false);
      setLocationActionInProgress(false);
    }
  }, [activeChatId, pendingNearbyRequest]);

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
    const handleKeyboardShow = (event: KeyboardEvent) => {
      setIsKeyboardVisible(true);
      const keyboardTop = event.endCoordinates?.screenY ?? 0;
      const keyboardHeight = event.endCoordinates?.height ?? 0;
      const windowHeight = Dimensions.get("window").height;
      const nextKeyboardOverlap = keyboardTop > 0 ? Math.max(0, windowHeight - keyboardTop) : 0;

      // RN Android keyboard metrics differ by nav mode/device; use a resilient overlap fallback.
      setKeyboardOverlap(
        Math.max(
          keyboardHeight,
          nextKeyboardOverlap
        )
      );
    };
    const handleKeyboardHide = () => {
      setIsKeyboardVisible(false);
      setKeyboardOverlap(0);
    };

    const showSubscription = Keyboard.addListener(showEvent, handleKeyboardShow);
    const hideSubscription = Keyboard.addListener(hideEvent, handleKeyboardHide);
    return () => {
      showSubscription.remove();
      hideSubscription.remove();
    };
  }, [hideEvent, showEvent]);

  const activeChat = useMemo(
    () => chats.find((item) => item.id === activeChatId) ?? chats[0],
    [activeChatId, chats]
  );

  useEffect(() => {
    if (
      !isReady
      || !userId
      || loadedHistoryUserIdRef.current !== userId
      || !activeChat
      || activeChat.messagesLoaded
      || !activeChat.conversationThreadId
    ) {
      return;
    }

    let cancelled = false;
    setIsConversationLoading(true);
    setSendError(null);

    void loadCompanionChatMessages(activeChat)
      .then((loadedChat) => {
        if (cancelled || loadedHistoryUserIdRef.current !== userId) {
          return;
        }

        setChats((current) =>
          current.map((chat) => chat.id === loadedChat.id ? loadedChat : chat)
        );
      })
      .catch((error) => {
        if (!cancelled && loadedHistoryUserIdRef.current === userId) {
          setSendError(formatUnknownError(error));
        }
      })
      .finally(() => {
        if (!cancelled && loadedHistoryUserIdRef.current === userId) {
          setIsConversationLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [activeChat, isReady, userId]);

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

  const appendMessageToChat = useCallback((chatId: string, message: CompanionMessage) => {
    setChats((current) => {
      const nextUpdatedUtc = new Date().toISOString();
      const updated = current.map((chat) => {
        if (chat.id !== chatId) {
          return chat;
        }

        const messages = [...chat.messages, message];
        return {
          ...chat,
          messages,
          title: formatChatTitle({ ...chat, messages }),
          updatedUtc: nextUpdatedUtc
        };
      });

      return sortChatsByPinAndRecency(updated);
    });
  }, []);

  const applySuggestedStateUpdatesToChat = useCallback((
    chatId: string,
    updates: Record<string, string>
  ) => {
    if (!updates || Object.keys(updates).length === 0) {
      return;
    }

    setChats((current) =>
      sortChatsByPinAndRecency(
        current.map((chat) => {
          if (chat.id !== chatId) {
            return chat;
          }

          const shouldClearResultContext =
            updates.active_result_set_clear?.trim().toLowerCase() === "true"
            || updates.result_context_clear?.trim().toLowerCase() === "true";
          const activeResultSetIdFromUpdate =
            updates.active_result_set_id?.trim()
            || updates.result_context_active_result_set_id?.trim()
            || null;
          const activeResultSetId = shouldClearResultContext
            ? null
            : activeResultSetIdFromUpdate ?? chat.activeResultSetId;
          const shouldClearSelectedEntity =
            shouldClearResultContext
            || updates.selected_entity_clear?.trim().toLowerCase() === "true";
          const selectedEntityId = shouldClearSelectedEntity
            ? null
            : updates.selected_entity_id?.trim() ?? chat.selectedEntityId;
          const clearPending = updates.pending_clarification_clear === "true";
          const pendingClarificationSlot = clearPending
            ? null
            : updates.pending_clarification_slot?.trim()
              ?? chat.pendingClarificationSlot;
          const pendingClarificationPromptIntent = clearPending
            ? null
            : updates.pending_clarification_prompt_intent?.trim()
              ?? chat.pendingClarificationPromptIntent;

          return {
            ...chat,
            activeResultSetId: activeResultSetId || null,
            selectedEntityId: selectedEntityId || null,
            pendingClarificationSlot: pendingClarificationSlot || null,
            pendingClarificationPromptIntent: pendingClarificationPromptIntent || null
          };
        })
      )
    );
  }, []);

  const setChatThreadId = useCallback((chatId: string, conversationThreadId: string | null) => {
    if (!conversationThreadId) {
      return;
    }

    setChats((current) =>
      sortChatsByPinAndRecency(
        current.map((chat) =>
          chat.id === chatId
            ? {
                ...chat,
                conversationThreadId
              }
            : chat
        )
      )
    );
  }, []);

  const sendChatRequest = useCallback(async (
    prompt: string,
    locationContext?: ChatLocationContext | null,
    diagnosticsMetadata?: Record<string, string> | null
  ) => {
    const trimmedPrompt = prompt.trim();
    if (!trimmedPrompt || !activeChat || !activeChat.messagesLoaded || isConversationLoading) {
      return;
    }

    if (isSending) {
      return;
    }

    const retryCandidate = failedSendRef.current;
    const reusingFailedRequest =
      Boolean(retryCandidate) &&
      retryCandidate?.chatId === activeChat.id &&
      retryCandidate?.message === trimmedPrompt;
    const clientRequestId =
      retryCandidate &&
      retryCandidate.chatId === activeChat.id &&
      retryCandidate.message === trimmedPrompt
        ? retryCandidate.clientRequestId
        : createClientRequestId();

    if (!reusingFailedRequest) {
      const now = new Date().toISOString();
      const userMessage: CompanionMessage = {
        id: `${Date.now()}-u`,
        role: "user",
        text: trimmedPrompt,
        createdUtc: now
      };
      appendMessageToChat(activeChat.id, userMessage);
    }

    setInput("");
    setSendError(null);
    setSendInfo(null);
    setIsSending(true);
    setSendingChatId(activeChat.id);

    try {
      const locationMetadata = locationContext
        ? buildChatLocationMetadata(locationContext)
        : null;
      const contextMetadata: Record<string, string> = {};
      if (activeChat.activeResultSetId) {
        contextMetadata.chat_result_set_id = activeChat.activeResultSetId;
      }

      if (activeChat.selectedEntityId) {
        contextMetadata.chat_selected_entity_id = activeChat.selectedEntityId;
      }

      const mergedMetadata = {
        ...contextMetadata,
        ...(diagnosticsMetadata ?? {}),
        ...(locationMetadata ?? {})
      };
      const locationState = locationContext
        ? buildChatLocationState(locationContext)
        : null;
      console.info("[CompanionChatLocation]", {
        event: "chat_send_location_mode",
        mode: locationContext?.source ?? "none",
        hasLocationDiagnostics: Boolean(diagnosticsMetadata && Object.keys(diagnosticsMetadata).length > 0)
      });
      const response = await sendAIChatMessage({
        message: trimmedPrompt,
        clientRequestId,
        conversationThreadId: activeChat.conversationThreadId,
        requirePersistentMemory: true,
        allowFallbackOnPersistentFailure: false,
        state: locationState,
        metadata: Object.keys(mergedMetadata).length > 0 ? mergedMetadata : null
      });

      setChatThreadId(activeChat.id, response.conversationThreadId);
      applySuggestedStateUpdatesToChat(activeChat.id, response.suggestedStateUpdates);

      if (response.inProgress) {
        setSendInfo("Assistant response is still in progress. Please retry in a moment.");
      } else if (response.deduped) {
        setSendInfo("Duplicate send avoided. Showing the existing turn result.");
      } else {
        setSendInfo(null);
      }

      const replyText = response.message?.trim();
      if (replyText) {
        const assistantMessage: CompanionMessage = {
          id: `${Date.now()}-a`,
          role: "assistant",
          text: replyText,
          createdUtc: new Date().toISOString(),
          structuredResults: response.structuredResults ?? null
        };
        appendMessageToChat(activeChat.id, assistantMessage);
      } else if (!response.inProgress) {
        setSendError("Assistant returned an empty reply. Please retry.");
        failedSendRef.current = {
          chatId: activeChat.id,
          message: trimmedPrompt,
          clientRequestId
        };
        return;
      }

      if (!response.succeeded || response.failureCode || response.failureReason) {
        const failureMessage = response.failureReason || response.failureCode || "Chat request failed.";
        setSendError(failureMessage);
        failedSendRef.current = {
          chatId: activeChat.id,
          message: trimmedPrompt,
          clientRequestId
        };
        return;
      }

      failedSendRef.current = null;
    } catch (error) {
      const readable = formatUnknownError(error);
      setSendError(readable);
      setSendInfo(null);
      failedSendRef.current = {
        chatId: activeChat.id,
        message: trimmedPrompt,
        clientRequestId
      };
    } finally {
      setIsSending(false);
      setSendingChatId(null);
    }
  }, [activeChat, appendMessageToChat, applySuggestedStateUpdatesToChat, isConversationLoading, isSending, setChatThreadId]);

  const queueNearbyGroundingPrompt = useCallback((
    prompt: string,
    permissionState: string,
    diagnosticsMetadata: Record<string, string>
  ) => {
    if (!activeChat) {
      return;
    }

    setPendingNearbyRequest({
      chatId: activeChat.id,
      prompt,
      diagnosticsMetadata
    });
    setTypedAreaInput("");
    setSendError(NEARBY_GROUNDING_FAIL_LOUD_MESSAGE);

    if (permissionState === "granted") {
      setTypedAreaPromptVisible(true);
      return;
    }

    if (permissionState === "denied_can_ask_again" || permissionState === "unknown") {
      setNearbyPermissionPromptVisible(true);
      return;
    }

    setLocationSettingsPromptVisible(true);
  }, [activeChat]);

  const resolveLatestPermissionState = useCallback(async () => {
    const first = await getForegroundLocationPermissionState();
    if (first !== "unknown") {
      return first;
    }

    const second = await getForegroundLocationPermissionState();
    console.info("[CompanionChatLocation]", {
      event: "chat_grounding_permission_rechecked",
      initialState: first,
      finalState: second
    });
    return second;
  }, []);

  useFocusEffect(
    useCallback(() => {
      let cancelled = false;
      const syncPermissionAfterSettingsReturn = async () => {
        if (!pendingNearbyRequest || !locationSettingsPromptVisible) {
          return;
        }

        const latestPermissionState = await resolveLatestPermissionState();
        if (cancelled || latestPermissionState !== "granted") {
          return;
        }

        setPendingNearbyRequest((current) => {
          if (!current) {
            return current;
          }

          return {
            ...current,
            diagnosticsMetadata: {
              ...current.diagnosticsMetadata,
              chat_location_permission_state: "granted"
            }
          };
        });
        setLocationSettingsPromptVisible(false);
        setNearbyPermissionPromptVisible(true);
      };

      void syncPermissionAfterSettingsReturn();
      return () => {
        cancelled = true;
      };
    }, [locationSettingsPromptVisible, pendingNearbyRequest, resolveLatestPermissionState])
  );

  const sendWithGrounding = useCallback(async (
    prompt: string,
    options?: {
      forcedLocationContext?: ChatLocationContext | null;
      diagnosticsMetadata?: Record<string, string> | null;
    }
  ) => {
    const trimmedPrompt = prompt.trim();
    if (!trimmedPrompt || !activeChat) {
      return;
    }

    const forcedContext = options?.forcedLocationContext ?? null;
    const forcedDiagnostics = options?.diagnosticsMetadata ?? null;

    if (forcedContext) {
      console.info("[CompanionChatLocation]", {
        event: "chat_grounding_attempt_skipped_reason",
        reason: "pre_resolved_context"
      });
      console.info("[CompanionChatLocation]", {
        event: "chat_grounding_final_state",
        state: forcedContext.source === "gps" ? "gps" : "typed"
      });
      await sendChatRequest(trimmedPrompt, forcedContext, forcedDiagnostics);
      return;
    }

    const permissionState = await resolveLatestPermissionState();
    console.info("[CompanionChatLocation]", {
      event: "chat_prompt_permission_state",
      permissionState
    });
    console.info("[CompanionChatLocation]", {
      event: "chat_grounding_attempt_started"
    });
    const attachment = await resolveChatLocationAttachment(
      trimmedPrompt,
      permissionState,
      getFreshForegroundLocationSnapshot
    );
    const mergedDiagnostics = {
      ...(forcedDiagnostics ?? {}),
      ...attachment.diagnosticsMetadata
    };
    if (attachment.context) {
      console.info("[CompanionChatLocation]", {
        event: "chat_grounding_final_state",
        state: attachment.context.source
      });
      await sendChatRequest(trimmedPrompt, attachment.context, mergedDiagnostics);
      return;
    }

    if (attachment.requiresNearbyClarification) {
      console.info("[CompanionChatLocation]", {
        event: "chat_grounding_attempt_skipped_reason",
        reason: permissionState === "granted"
          ? "gps_resolution_failed"
          : "permission_not_granted",
        permissionState,
        refreshOutcome: attachment.diagnosticsMetadata.chat_location_refresh_outcome
      });
      console.info("[CompanionChatLocation]", {
        event: "chat_grounding_final_state",
        state: "missing"
      });
      queueNearbyGroundingPrompt(trimmedPrompt, permissionState, mergedDiagnostics);
      return;
    }

    console.info("[CompanionChatLocation]", {
      event: "chat_grounding_final_state",
      state: "none"
    });
    await sendChatRequest(trimmedPrompt, null, mergedDiagnostics);
  }, [activeChat, queueNearbyGroundingPrompt, resolveLatestPermissionState, sendChatRequest]);

  const clearPendingNearbyRequest = useCallback(() => {
    setPendingNearbyRequest(null);
    setTypedAreaInput("");
    setLocationActionInProgress(false);
    setNearbyPermissionPromptVisible(false);
    setLocationSettingsPromptVisible(false);
    setTypedAreaPromptVisible(false);
  }, []);

  const handleAllowNearbyPermission = useCallback(async () => {
    if (!pendingNearbyRequest || locationActionInProgress) {
      return;
    }

    setLocationActionInProgress(true);
    const result = await requestForegroundLocationAccess();
    console.info("[CompanionChatLocation]", {
      event: "nearby_permission_request_result",
      permissionState: result.permissionState
    });
    if (result.permissionState === "granted" && result.snapshot) {
      const prompt = pendingNearbyRequest.prompt;
      const diagnostics = buildNearbyGroundingDiagnosticsMetadata(result.permissionState, {
        context: {
          source: "gps",
          latitude: result.snapshot.latitude,
          longitude: result.snapshot.longitude,
          accuracyMeters: result.snapshot.accuracyMeters,
          capturedAtUtc: result.snapshot.capturedAtUtc,
          localityLabel: result.snapshot.localityLabel
        },
        refreshAttempted: true,
        outcome: "success"
      });
      clearPendingNearbyRequest();
      await sendWithGrounding(prompt, {
        forcedLocationContext: {
          source: "gps",
          latitude: result.snapshot.latitude,
          longitude: result.snapshot.longitude,
          accuracyMeters: result.snapshot.accuracyMeters,
          capturedAtUtc: result.snapshot.capturedAtUtc,
          localityLabel: result.snapshot.localityLabel
        },
        diagnosticsMetadata: diagnostics
      });
      return;
    }

    setPendingNearbyRequest((current) => {
      if (!current) {
        return current;
      }

      return {
        ...current,
        diagnosticsMetadata: buildNearbyGroundingDiagnosticsMetadata(result.permissionState, {
          context: null,
          refreshAttempted: true,
          outcome: "failed"
        })
      };
    });
    setLocationActionInProgress(false);
    setNearbyPermissionPromptVisible(false);
    if (result.permissionState === "denied_open_settings" || result.permissionState === "unavailable") {
      setLocationSettingsPromptVisible(true);
      return;
    }

    setTypedAreaPromptVisible(true);
  }, [clearPendingNearbyRequest, locationActionInProgress, pendingNearbyRequest, sendWithGrounding]);

  const handleOpenLocationSettings = useCallback(async () => {
    await openLocationSettings();
  }, []);

  const handleUseTypedAreaForPendingPrompt = useCallback(async () => {
    if (!pendingNearbyRequest) {
      return;
    }

    const normalizedArea = normalizeTypedArea(typedAreaInput);
    if (!normalizedArea) {
      return;
    }

    const prompt = pendingNearbyRequest.prompt;
    const diagnostics = pendingNearbyRequest.diagnosticsMetadata;
    clearPendingNearbyRequest();
    await sendWithGrounding(prompt, {
      forcedLocationContext: {
        source: "typed_area",
        typedArea: normalizedArea
      },
      diagnosticsMetadata: diagnostics
    });
  }, [clearPendingNearbyRequest, pendingNearbyRequest, sendWithGrounding, typedAreaInput]);

  const startNewChat = () => {
    const nextChat = createChat();
    setChats((current) => sortChatsByPinAndRecency([nextChat, ...current]));
    setActiveChatId(nextChat.id);
    setInput("");
    setSendError(null);
    setSendInfo(null);
    setHistoryVisible(false);
    setDefaultPromptSet((current) => rotateStarterPrompts(current));
  };

  const removeChat = async (chatId: string) => {
    if (!userId) {
      return;
    }

    const chat = chats.find((candidate) => candidate.id === chatId);
    try {
      if (chat?.conversationThreadId) {
        await archiveAIChatThread(chat.conversationThreadId);
      }

      const updated = sortChatsByPinAndRecency(
        await deleteCompanionChat(userId, chatId, chats)
      );
      if (updated.length === 0) {
        const next = createChat();
        setChats([next]);
        setActiveChatId(next.id);
        return;
      }

      setChats(updated);
      if (activeChatId === chatId) {
        setActiveChatId(updated[0].id);
      }
    } catch (error) {
      Alert.alert(
        "Conversation not removed",
        formatUnknownError(error)
      );
    }
  };

  const openEditChat = (chat: CompanionChat) => {
    setEditingChatId(chat.id);
    setEditingChatTitle(chat.title);
    setEditingChatColor(chat.color);
    setEditChatVisible(true);
  };

  const saveEditedChat = () => {
    if (!editingChatId) {
      setEditChatVisible(false);
      return;
    }

    const nextTitle = editingChatTitle.trim() || "New conversation";
    setChats((current) =>
      sortChatsByPinAndRecency(
        current.map((chat) =>
          chat.id === editingChatId
            ? {
                ...chat,
                title: nextTitle,
                color: editingChatColor
              }
            : chat
        )
      )
    );
    setEditChatVisible(false);
  };

  const togglePinChat = (chatId: string) => {
    const now = new Date().toISOString();
    setChats((current) =>
      sortChatsByPinAndRecency(
        current.map((chat) =>
          chat.id === chatId
            ? {
                ...chat,
                isPinned: !chat.isPinned,
                pinnedUtc: !chat.isPinned ? now : null
              }
            : chat
        )
      )
    );
  };

  const pinnedChats = useMemo(() => chats.filter((chat) => chat.isPinned), [chats]);
  const regularChats = useMemo(() => chats.filter((chat) => !chat.isPinned), [chats]);
  const pendingNearbyPermissionState =
    pendingNearbyRequest?.diagnosticsMetadata?.chat_location_permission_state ?? "unknown";
  const nearbyLocationPrimaryLabel =
    pendingNearbyPermissionState === "granted" ? "Use current location" : "Allow location";
  const nearbyLocationTitle =
    pendingNearbyPermissionState === "granted"
      ? "Use current location for nearby places"
      : "Allow location for nearby places";
  const nearbyLocationMessage =
    pendingNearbyPermissionState === "granted"
      ? "Location access is enabled. Use your current location now, or enter an area manually."
      : "This request needs location to find places near you. You can allow location now or enter an area manually.";

  const showPrompts = isKeyboardVisible && (isInputFocused || input.trim().length > 0);
  const promptSuggestions = useMemo(() => {
    if (!showPrompts) {
      return [];
    }

    if (!input.trim()) {
      return defaultPromptSet;
    }

    return rankPrompts(input);
  }, [defaultPromptSet, input, showPrompts]);
  const effectiveBottomInset =
    Platform.OS === "android" ? getEffectiveBottomSystemInset(insets.bottom) : insets.bottom;
  const closedInputBottomInset = Math.max(
    navigation.floatingTabBarHeight + spacing[8],
    getDockAwareContentBottomInset(insets.bottom) - spacing[4]
  );
  const keyboardInputBottomInset =
    Platform.OS === "android"
      ? Math.max(spacing[8], keyboardOverlap - effectiveBottomInset + spacing[8])
      : Math.max(spacing[20], insets.bottom + spacing[8]);
  const inputBottomInset = isKeyboardVisible
    ? keyboardInputBottomInset
    : closedInputBottomInset;
  const chatViewportInset =
    inputBottomInset +
    inputBarHeight +
    spacing[2] +
    (showPrompts ? promptLayerHeight + spacing[2] : 0);

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

      const nextOffset = promptOffsetRef.current + 1.7;
      if (nextOffset >= latestMaxOffset) {
        promptOffsetRef.current = 0;
        promptScrollRef.current?.scrollTo({ x: 0, animated: false });
        return;
      }

      promptOffsetRef.current = nextOffset;
      promptScrollRef.current?.scrollTo({ x: nextOffset, animated: false });
    }, 80);
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

  useEffect(() => {
    Animated.timing(inputBottomAnimated, {
      toValue: inputBottomInset,
      duration: 200,
      easing: Easing.out(Easing.cubic),
      useNativeDriver: false
    }).start();
  }, [inputBottomAnimated, inputBottomInset]);

  return (
    <ScreenContainer
      scrollable={false}
      contentStyle={styles.content}
      includeBottomSafeArea={false}
    >
      <KeyboardAvoidingView
        style={styles.keyboardWrap}
        behavior={Platform.OS === "ios" ? "padding" : undefined}
        keyboardVerticalOffset={Platform.OS === "ios" ? 8 + Math.max(insets.bottom, 0) : 0}
      >
        <HeaderShell
          preset="primaryDefault"
          includeTopInset
          bleedHorizontal={layout.screenHorizontalPadding}
          title="NS Companion"
          style={{
            marginTop: -insets.top,
          }}
          trailingAction={
            <HeaderActionButton
              icon={<Ionicons name="chatbubbles-outline" size={18} color={palette.textPrimary} />}
              onPress={() => setHistoryVisible(true)}
              accessibilityLabel="Open chats"
            />
          }
        />

        <View style={[styles.chatWrap, { marginBottom: chatViewportInset }]}>
          {isConversationLoading || activeChat?.messagesLoaded === false ? (
            <View style={styles.emptyWrap}>
              <ActivityIndicator color={palette.accent} />
              <Text style={styles.emptyIntro}>Opening conversation...</Text>
            </View>
          ) : activeChat?.messages.length ? (
            <FlatList
              ref={messageListRef}
              data={activeChat.messages}
              keyExtractor={(item) => item.id}
              contentContainerStyle={styles.chatList}
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
                <View
                  style={[
                    styles.chatRow,
                    item.role === "assistant" ? styles.assistantRow : styles.userRow
                  ]}
                >
                  <GlassCard
                    style={[
                      styles.chatBubble,
                      item.role === "assistant" ? styles.assistantBubble : styles.userBubble
                    ]}
                  >
                    <Text style={styles.chatText}>
                      {item.role === "assistant" && item.structuredResults?.type === "places"
                        ? getStructuredPlacesIntroText(item.text)
                        : item.text}
                    </Text>
                  </GlassCard>
                  {item.role === "assistant" && item.structuredResults?.type === "places" ? (
                    <PlaceCardCarousel places={item.structuredResults.items} />
                  ) : null}
                </View>
              )}
            />
          ) : (
            <View style={styles.emptyWrap}>
              <Text style={styles.emptyIntro}>{introPair.intro}</Text>
            </View>
          )}
        </View>

        <Animated.View style={[styles.inputArea, { bottom: inputBottomAnimated }]} pointerEvents="box-none">
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
                    disabled={isSending}
                    style={({ pressed }) => [
                      styles.promptChip,
                      isSending ? styles.promptChipDisabled : null,
                      pressed ? styles.promptChipPressed : null
                    ]}
                    onPress={() => {
                      void sendWithGrounding(prompt);
                    }}
                  >
                    <Text style={styles.promptChipText}>{prompt}</Text>
                  </Pressable>
                ))}
              </ScrollView>
            </View>
          ) : null}

          {sendingChatId === activeChat?.id && isSending ? (
            <Text style={styles.requestInfoText}>Sending request...</Text>
          ) : null}

          {sendInfo ? <Text style={styles.requestInfoText}>{sendInfo}</Text> : null}
          {sendError ? <Text style={styles.requestErrorText}>{sendError}</Text> : null}

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
              selectionColor={palette.accent}
              cursorColor={palette.accent}
              style={styles.input}
              editable={!isConversationLoading && activeChat?.messagesLoaded !== false}
              multiline
              maxLength={280}
            />
            <Pressable
              disabled={isSending || isConversationLoading || activeChat?.messagesLoaded === false || !input.trim()}
              style={({ pressed }) => [
                styles.sendButton,
                isSending || isConversationLoading || activeChat?.messagesLoaded === false || !input.trim()
                  ? styles.sendButtonDisabled
                  : null,
                pressed ? styles.sendPressed : null
              ]}
              onPress={() => {
                void sendWithGrounding(input.trim());
              }}
            >
              <Ionicons
                name={isSending ? "time-outline" : "arrow-up"}
                size={16}
                color={palette.appBackground}
              />
            </Pressable>
          </View>
        </Animated.View>
      </KeyboardAvoidingView>

      <FloatingBottomNav
        items={appBottomNavItems}
        activeKey={activeBottomKey}
        onPressItem={(item) => {
          if (item.key === activeBottomKey) {
            return;
          }

          const href = {
            index: "/(tabs)",
            accounts: "/(tabs)/accounts",
            activity: "/(tabs)/activity",
            cashflow: "/(tabs)/cashflow"
          }[item.key];

          if (!href) {
            return;
          }

          navigateWithProbe(
            router as unknown as {
              push: (href: string) => void;
              replace: (href: string) => void;
              navigate?: (href: string) => void;
            },
            href,
            "companion-bottom-nav-item"
          );
        }}
      />

      <LocationPermissionPromptModal
        visible={nearbyPermissionPromptVisible}
        onRequestClose={clearPendingNearbyRequest}
        title={nearbyLocationTitle}
        message={nearbyLocationMessage}
        actions={[
          {
            label: nearbyLocationPrimaryLabel,
            onPress: () => {
              void handleAllowNearbyPermission();
            },
            variant: "primary",
            disabled: locationActionInProgress
          },
          {
            label: "Enter area manually",
            onPress: () => {
              setNearbyPermissionPromptVisible(false);
              setTypedAreaPromptVisible(true);
            },
            variant: "secondary",
            disabled: locationActionInProgress
          },
          {
            label: "Not now",
            onPress: clearPendingNearbyRequest,
            variant: "secondary",
            disabled: locationActionInProgress
          }
        ]}
      />

      <LocationPermissionPromptModal
        visible={locationSettingsPromptVisible}
        onRequestClose={clearPendingNearbyRequest}
        title="Enable location in Settings"
        message="Location permission is currently blocked by your OS. Open Settings to enable it, or type an area for this request."
        actions={[
          {
            label: "Open Settings",
            onPress: () => {
              void handleOpenLocationSettings();
            },
            variant: "primary"
          },
          {
            label: "Enter area manually",
            onPress: () => {
              setLocationSettingsPromptVisible(false);
              setTypedAreaPromptVisible(true);
            },
            variant: "secondary"
          },
          {
            label: "Not now",
            onPress: clearPendingNearbyRequest,
            variant: "secondary"
          }
        ]}
      />

      <LocationTypedAreaModal
        visible={typedAreaPromptVisible}
        value={typedAreaInput}
        onChangeValue={setTypedAreaInput}
        onCancel={clearPendingNearbyRequest}
        onConfirm={() => {
          void handleUseTypedAreaForPendingPrompt();
        }}
      />

      <SystemModal visible={historyVisible} transparent animationType="fade" onRequestClose={() => setHistoryVisible(false)}>
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
              {pinnedChats.map((chat) => {
                const theme = getChatColorTheme(chat.color, chatColorThemes);
                return (
                  <View
                    key={chat.id}
                    style={[
                      styles.historyItem,
                      {
                        borderColor: theme.borderColor
                      },
                      chat.id === activeChat?.id ? styles.historyItemActive : null
                    ]}
                  >
                    <Pressable
                      onPress={() => {
                        setActiveChatId(chat.id);
                        setHistoryVisible(false);
                      }}
                      style={({ pressed }) => [styles.historySelectArea, pressed ? styles.historyItemPressed : null]}
                    >
                      <Text style={styles.historyItemTitle}>{chat.title}</Text>
                      <Text style={styles.historyItemMeta}>{new Date(chat.updatedUtc).toLocaleString("en-IE")}</Text>
                    </Pressable>
                    <View style={styles.historyActionGroup}>
                      <Pressable
                        style={({ pressed }) => [styles.historyEditButton, pressed ? styles.historyItemPressed : null]}
                        onPress={() => openEditChat(chat)}
                      >
                        <Ionicons name="color-palette-outline" size={15} color={palette.textPrimary} />
                      </Pressable>
                      <Pressable
                        style={({ pressed }) => [
                          styles.historyPinButton,
                          chat.isPinned ? styles.historyPinButtonActive : null,
                          pressed ? styles.historyItemPressed : null
                        ]}
                        onPress={() => togglePinChat(chat.id)}
                      >
                        <MaterialCommunityIcons
                          name={chat.isPinned ? "pin" : "pin-off-outline"}
                          size={15}
                          color={chat.isPinned ? palette.textPrimary : palette.textSecondary}
                        />
                      </Pressable>
                      <Pressable
                        style={({ pressed }) => [styles.historyDeleteButton, pressed ? styles.historyItemPressed : null]}
                        onPress={() => {
                          Alert.alert(
                            "Delete chat?",
                            "This deletes app-stored messages and app-owned memory artifacts for this chat.",
                            [
                              { text: "Cancel", style: "cancel" },
                              {
                                text: "Delete",
                                style: "destructive",
                                onPress: () => {
                                  void removeChat(chat.id);
                                }
                              }
                            ]
                          );
                        }}
                      >
                        <Ionicons name="trash-outline" size={14} color={palette.negative} />
                      </Pressable>
                    </View>
                  </View>
                );
              })}

              {pinnedChats.length > 0 && regularChats.length > 0 ? (
                <View style={styles.historyPinnedSpacer}>
                  <View style={styles.historyPinnedDivider} />
                  <Text style={styles.historyPinnedLabel}>Other chats</Text>
                </View>
              ) : null}

              {regularChats.map((chat) => {
                const theme = getChatColorTheme(chat.color, chatColorThemes);
                return (
                  <View
                    key={chat.id}
                    style={[
                      styles.historyItem,
                      {
                        borderColor: theme.borderColor
                      },
                      chat.id === activeChat?.id ? styles.historyItemActive : null
                    ]}
                  >
                    <Pressable
                      onPress={() => {
                        setActiveChatId(chat.id);
                        setHistoryVisible(false);
                      }}
                      style={({ pressed }) => [styles.historySelectArea, pressed ? styles.historyItemPressed : null]}
                    >
                      <Text style={styles.historyItemTitle}>{chat.title}</Text>
                      <Text style={styles.historyItemMeta}>{new Date(chat.updatedUtc).toLocaleString("en-IE")}</Text>
                    </Pressable>
                    <View style={styles.historyActionGroup}>
                      <Pressable
                        style={({ pressed }) => [styles.historyEditButton, pressed ? styles.historyItemPressed : null]}
                        onPress={() => openEditChat(chat)}
                      >
                        <Ionicons name="color-palette-outline" size={15} color={palette.textPrimary} />
                      </Pressable>
                      <Pressable
                        style={({ pressed }) => [
                          styles.historyPinButton,
                          chat.isPinned ? styles.historyPinButtonActive : null,
                          pressed ? styles.historyItemPressed : null
                        ]}
                        onPress={() => togglePinChat(chat.id)}
                      >
                        <MaterialCommunityIcons
                          name={chat.isPinned ? "pin" : "pin-off-outline"}
                          size={15}
                          color={chat.isPinned ? palette.textPrimary : palette.textSecondary}
                        />
                      </Pressable>
                      <Pressable
                        style={({ pressed }) => [styles.historyDeleteButton, pressed ? styles.historyItemPressed : null]}
                        onPress={() => {
                          Alert.alert(
                            "Delete chat?",
                            "This deletes app-stored messages and app-owned memory artifacts for this chat.",
                            [
                              { text: "Cancel", style: "cancel" },
                              {
                                text: "Delete",
                                style: "destructive",
                                onPress: () => {
                                  void removeChat(chat.id);
                                }
                              }
                            ]
                          );
                        }}
                      >
                        <Ionicons name="trash-outline" size={14} color={palette.negative} />
                      </Pressable>
                    </View>
                  </View>
                );
              })}
            </ScrollView>
          </Pressable>
        </Pressable>
      </SystemModal>

      <SystemModal
        visible={editChatVisible}
        transparent
        animationType="fade"
        onRequestClose={() => setEditChatVisible(false)}
      >
        <Pressable style={styles.historyOverlay} onPress={() => setEditChatVisible(false)}>
          <Pressable style={styles.editSheet} onPress={() => undefined}>
            <Text style={styles.editTitle}>Edit chat</Text>
            <TextInput
              value={editingChatTitle}
              onChangeText={(next) => setEditingChatTitle(next.slice(0, CHAT_TITLE_MAX_LENGTH))}
              placeholder="Chat title"
              placeholderTextColor={palette.textSecondary}
              selectionColor={palette.accent}
              cursorColor={palette.accent}
              style={styles.editInput}
              maxLength={CHAT_TITLE_MAX_LENGTH}
            />
            <Text style={styles.editLabel}>Color</Text>
            <View style={styles.colorSwatchWrap}>
              {CHAT_COLOR_ORDER.map((colorKey) => {
                const theme = getChatColorTheme(colorKey, chatColorThemes);
                const selected = editingChatColor === colorKey;
                return (
                  <Pressable
                    key={colorKey}
                    onPress={() => setEditingChatColor(colorKey)}
                    style={({ pressed }) => [
                      styles.colorSwatchButton,
                      selected ? styles.colorSwatchButtonSelected : null,
                      pressed ? styles.historyItemPressed : null
                    ]}
                    accessibilityRole="button"
                    accessibilityLabel={`Select ${theme.label} color`}
                  >
                    <View
                      style={[
                        styles.colorSwatchDot,
                        {
                          backgroundColor: theme.swatchColor
                        }
                      ]}
                    />
                  </Pressable>
                );
              })}
            </View>
            <View style={styles.editActions}>
              <Pressable
                onPress={() => setEditChatVisible(false)}
                style={({ pressed }) => [styles.editCancelButton, pressed ? styles.historyItemPressed : null]}
              >
                <Text style={styles.editCancelText}>Cancel</Text>
              </Pressable>
              <PrimaryButton label="Save" onPress={saveEditedChat} />
            </View>
          </Pressable>
        </Pressable>
      </SystemModal>
    </ScreenContainer>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  content: {
    paddingBottom: 0
  },
  keyboardWrap: {
    flex: 1
  },
  chatWrap: {
    flex: 1,
    marginTop: 0
  },
  chatList: {
    gap: spacing[12],
    paddingTop: 0,
    flexGrow: 1,
    justifyContent: "flex-end"
  },
  chatRow: {
    width: "100%"
  },
  assistantRow: {
    alignItems: "flex-start"
  },
  userRow: {
    alignItems: "flex-end"
  },
  chatBubble: {
    gap: spacing[6],
    maxWidth: "70%",
    minHeight: 0,
    width: "auto",
    paddingHorizontal: spacing[16],
    paddingVertical: spacing[12]
  },
  userBubble: {
    borderColor: palette.borderStrong,
    backgroundColor: surfaces.fieldStrong
  },
  assistantBubble: {
    borderColor: "rgba(255,190,122,0.3)",
    backgroundColor: surfaces.card
  },
  chatText: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "400",
    lineHeight: 20
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
    fontWeight: "600",
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
    minHeight: sizing.chip.heights.standard,
    borderRadius: radius.pill,
    borderWidth: 1,
    borderColor: "rgba(242,140,40,0.2)",
    backgroundColor: surfaces.fieldStrong,
    justifyContent: "center",
    paddingHorizontal: sizing.chip.horizontalPadding.standard
  },
  promptChipPressed: {
    opacity: 0.9,
    transform: [{ scale: 0.99 }]
  },
  promptChipDisabled: {
    opacity: 0.55
  },
  promptChipText: {
    color: palette.textSecondary,
    ...typography.caption,
    lineHeight: 15
  },
  inputBar: {
    borderRadius: radius.medium,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    flexDirection: "row",
    alignItems: "flex-end",
    paddingLeft: spacing[12],
    paddingRight: spacing[8],
    paddingVertical: spacing[8],
    minHeight: controls.fieldHeight,
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
    width: sizing.button.heights.compact,
    height: sizing.button.heights.compact,
    borderRadius: radius.small,
    backgroundColor: palette.primaryGlow,
    alignItems: "center",
    justifyContent: "center",
    marginBottom: 2
  },
  sendButtonDisabled: {
    opacity: 0.45
  },
  sendPressed: {
    opacity: 0.82,
    transform: [{ scale: 0.96 }]
  },
  requestInfoText: {
    color: palette.textSecondary,
    ...typography.caption,
    paddingHorizontal: spacing[2]
  },
  requestErrorText: {
    color: palette.negative,
    ...typography.caption,
    paddingHorizontal: spacing[2]
  },
  historyOverlay: {
    flex: 1,
    backgroundColor: palette.overlay,
    justifyContent: "flex-end"
  },
  historySheet: {
    borderTopLeftRadius: radius.hero,
    borderTopRightRadius: radius.hero,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.sheet,
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
    borderRadius: radius.small,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    paddingVertical: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  historyActionGroup: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  historySelectArea: {
    flex: 1,
    gap: spacing[4]
  },
  historyEditButton: {
    width: sizing.chip.heights.large,
    height: sizing.chip.heights.large,
    borderRadius: radius.small,
    borderWidth: 1,
    borderColor: "rgba(242,140,40,0.2)",
    backgroundColor: surfaces.fieldStrong,
    alignItems: "center",
    justifyContent: "center"
  },
  historyPinButton: {
    width: sizing.chip.heights.large,
    height: sizing.chip.heights.large,
    borderRadius: radius.small,
    borderWidth: 1,
    borderColor: "rgba(242,140,40,0.2)",
    backgroundColor: surfaces.fieldStrong,
    alignItems: "center",
    justifyContent: "center"
  },
  historyPinButtonActive: {
    borderColor: "rgba(242,140,40,0.52)",
    backgroundColor: "rgba(242,140,40,0.2)"
  },
  historyDeleteButton: {
    width: sizing.chip.heights.large,
    height: sizing.chip.heights.large,
    borderRadius: radius.small,
    borderWidth: 1,
    borderColor: "rgba(244,104,119,0.35)",
    backgroundColor: "rgba(90,16,30,0.24)",
    alignItems: "center",
    justifyContent: "center"
  },
  historyItemActive: {
    shadowColor: palette.primaryGlow,
    shadowOpacity: 0.18,
    shadowRadius: 8,
    shadowOffset: { width: 0, height: 2 },
    elevation: 2
  },
  historyItemPressed: {
    opacity: 0.9
  },
  historyPinnedSpacer: {
    paddingVertical: spacing[8],
    gap: spacing[8]
  },
  historyPinnedDivider: {
    height: 1,
    backgroundColor: "rgba(242,140,40,0.2)"
  },
  historyPinnedLabel: {
    color: palette.textSecondary,
    ...typography.caption,
    letterSpacing: 0.3
  },
  historyItemTitle: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  historyItemMeta: {
    color: palette.textSecondary,
    ...typography.caption
  },
  editSheet: {
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.sheet,
    marginHorizontal: spacing[20],
    padding: spacing[16],
    gap: spacing[12]
  },
  editTitle: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  editLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  editInput: {
    minHeight: controls.fieldHeight,
    borderRadius: radius.small,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    color: palette.textPrimary,
    ...typography.body1
  },
  colorSwatchWrap: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: spacing[8]
  },
  colorSwatchButton: {
    width: sizing.button.heights.compact,
    height: sizing.button.heights.compact,
    borderRadius: radius.pill,
    borderWidth: 1,
    borderColor: "rgba(242,140,40,0.22)",
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: surfaces.field
  },
  colorSwatchButtonSelected: {
    borderColor: palette.textPrimary,
    borderWidth: 2
  },
  colorSwatchDot: {
    width: 20,
    height: 20,
    borderRadius: 6
  },
  editActions: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  editCancelButton: {
    minHeight: sizing.button.heights.standard,
    minWidth: 94,
    borderRadius: radius.small,
    borderWidth: 1,
    borderColor: "rgba(242,140,40,0.2)",
    backgroundColor: surfaces.fieldStrong,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[12]
  },
  editCancelText: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  }
}));


