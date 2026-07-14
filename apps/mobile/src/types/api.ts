export type AccountType = "Current" | "Savings" | "Credit" | "Cash" | "Other";
export type TransactionDirection = "Income" | "Expense";

export type AccountBalanceSource = "provider_snapshot" | "manual_ledger" | "unavailable";
export type AccountBalanceFreshness = "fresh" | "stale" | "current" | "unknown";

export type AccountBalanceDto = {
  current: number | null;
  available: number | null;
  overdraft: number | null;
  currency: string;
  source: AccountBalanceSource;
  asOfUtc: string | null;
  freshness: AccountBalanceFreshness;
  exclusions: string[];
};

export type CurrencyBalanceTotalDto = {
  currency: string;
  amount: number;
  accountCount: number;
  basis: "current";
};

export type PortfolioBalanceDto = {
  byCurrency: CurrencyBalanceTotalDto[];
  includedAccountCount: number;
  excludedAccountCount: number;
  hasMultipleCurrencies: boolean;
};

export type AccountDto = {
  id: string;
  name: string;
  type: AccountType;
  currency: string;
  currentBalance: number;
  transactionCount: number;
  createdUtc: string;
  providerId: string | null;
  providerDisplayName: string | null;
  providerIconUrl: string | null;
  providerLogoUrl: string | null;
  providerBrandBgColor: string | null;
  hasProviderBranding: boolean;
  balance?: AccountBalanceDto | null;
};

export type CreateAccountRequest = {
  name: string;
  type: AccountType;
  currency: string;
  openingBalance?: number | null;
};

export type UpdateAccountRequest = {
  name: string;
  type: AccountType;
};

export type TransactionDto = {
  id: string;
  accountId: string;
  accountName: string;
  description: string;
  amount: number;
  currency: string;
  categoryId: string | null;
  categoryName: string | null;
  taxonomyDomainId: number | null;
  taxonomyDomainName: string | null;
  taxonomyCategoryId: number | null;
  taxonomyCategoryName: string | null;
  taxonomySubcategoryId: number | null;
  taxonomySubcategoryName: string | null;
  transferKind?:
    | "manual_transfer"
    | "linked_internal_transfer"
    | "savings_roundup"
    | "savings_manual_deposit"
    | "savings_manual_withdrawal"
    | null;
  linkedTransferTransactionId?: string | null;
  deterministicClassificationStatus:
    | "not_evaluated"
    | "evaluating"
    | "classified_matched_rule"
    | "evaluated_no_matching_rule"
    | "deferred_waiting_for_counterparty"
    | "deferred_waiting_for_more_context"
    | "rejected_ambiguous_match"
    | "superseded_recompute_required";
  deterministicClassificationTerminal: boolean;
  deterministicClassificationVersion?: number | null;
  deterministicClassificationRuleKey?: string | null;
  deterministicClassificationReasonCode?: string | null;
  deterministicClassificationEvidenceJson?: string | null;
  deterministicDeferredRetryEligible?: boolean;
  deterministicLinkedTransactionId?: string | null;
  deterministicRelationshipType?: "internal_transfer" | "savings_transfer" | null;
  deterministicRelationshipGroupId?: string | null;
  relationshipType?:
    | "internal_account_transfer"
    | "savings_roundup"
    | "savings_manual_deposit"
    | "savings_manual_withdrawal"
    | "possible_transfer_suggestion"
    | "possible_savings_suggestion"
    | null;
  relationshipStatus?: "active" | "suggested" | "dismissed" | null;
  relationshipDirection?: "outflow_to_inflow" | "outflow_to_savings" | "inflow_from_savings" | null;
  relationshipConfidenceScore?: number | null;
  relationshipConfidenceTier?: "low" | "medium" | "high" | null;
  relationshipAnalyticsTreatment?: string | null;
  relationshipVirtualDestinationLabel?: string | null;
  relationshipCounterpartyTransactionId?: string | null;
  displaySemantic?: "real_transaction" | "internal_transfer" | "savings_roundup" | "savings_manual_move" | null;
  transferPolicyKind?: string | null;
  reportingBucket?: string | null;
  isGloballyNeutralized?: boolean | null;
  reason: string | null;
  notes: string | null;
  bookedAtUtc: string;
  createdUtc: string;
  metadataUpdatedUtc: string | null;
  direction: TransactionDirection;
};

export type TransactionPageDirection = "income" | "expense";

export type TransactionPageRequest = {
  pageSize?: number | null;
  cursor?: string | null;
  accountId?: string | null;
  fromUtc?: string | null;
  toUtc?: string | null;
  direction?: TransactionPageDirection | null;
};

export type TransactionPageFiltersDto = {
  accountId: string | null;
  fromUtc: string | null;
  toUtc: string | null;
  direction: TransactionPageDirection | null;
};

export type TransactionPageDto = {
  items: TransactionDto[];
  nextCursor: string | null;
  hasMore: boolean;
  pageSize: number;
  filters: TransactionPageFiltersDto;
};

export type CreateTransactionRequest = {
  accountId: string;
  description: string;
  amount: number;
  direction: TransactionDirection;
  currency?: string | null;
  categoryId?: string | null;
  bookedAtUtc?: string | null;
};

export type UpdateTransactionMetadataRequest = {
  reason?: string | null;
  notes?: string | null;
  taxonomyCategoryId: number;
  taxonomySubcategoryId?: number | null;
};

export type ExpenseTrackerEntryStatus = "planned" | "completed";

export type ExpenseTaxonomySubcategoryDto = {
  id: number;
  domainId: number;
  categoryId: number;
  name: string;
  description: string;
  isUserSelectable: boolean;
  sortOrder: number;
  isActive: boolean;
  aliases: string[];
  keywords: string[];
  merchantHints: string[];
  isLikelyRecurring: boolean;
  isLikelyRefundable: boolean;
  notes: string | null;
};

export type ExpenseTaxonomyCategoryDto = {
  id: number;
  domainId: number;
  name: string;
  description: string;
  isUserSelectable: boolean;
  sortOrder: number;
  isActive: boolean;
  aliases: string[];
  keywords: string[];
  merchantHints: string[];
  isLikelyRecurring: boolean;
  isLikelyRefundable: boolean;
  notes: string | null;
  subcategories: ExpenseTaxonomySubcategoryDto[];
};

export type ExpenseTaxonomyDomainDto = {
  id: number;
  name: string;
  description: string;
  isUserSelectable: boolean;
  isSystemDomain: boolean;
  sortOrder: number;
  isActive: boolean;
  aliases: string[];
  keywords: string[];
  merchantHints: string[];
  isLikelyRecurring: boolean;
  isLikelyRefundable: boolean;
  notes: string | null;
  categories: ExpenseTaxonomyCategoryDto[];
};

export type ExpenseTaxonomyResponseDto = {
  version: string;
  domains: ExpenseTaxonomyDomainDto[];
};

export type ExpenseTrackerEntryDto = {
  id: string;
  title: string;
  amount: number;
  currency: string;
  domainId: number | null;
  domainName: string | null;
  categoryId: number | null;
  categoryName: string | null;
  subcategoryId: number | null;
  subcategoryName: string | null;
  categoryLabel: string | null;
  paymentSource: string;
  occurredAtUtc: string;
  notes: string | null;
  tags: string[];
  status: ExpenseTrackerEntryStatus;
  isRecurring: boolean;
  merchant: string | null;
  createdUtc: string;
  updatedUtc: string;
};

export type CreateExpenseTrackerEntryRequest = {
  title: string;
  amount: number;
  currency: string;
  subcategoryId: number;
  paymentSource: string;
  occurredAtUtc?: string | null;
  notes?: string | null;
  tags?: string[] | null;
  status: ExpenseTrackerEntryStatus;
  isRecurring: boolean;
  merchant?: string | null;
};

export type UpdateExpenseTrackerEntryRequest = CreateExpenseTrackerEntryRequest;

export type CategoryDto = {
  id: string;
  name: string;
  type: string;
  createdUtc: string;
};

export type DashboardSummaryDto = {
  totalBalance: number;
  accountCount: number;
  transactionCount: number;
  recentOutflow: number;
  accountPreview: AccountDto[];
  recentTransactions: TransactionDto[];
  portfolioBalance?: PortfolioBalanceDto | null;
};

export type ValidationProblem = {
  title?: string;
  status?: number;
  errors?: Record<string, string[]>;
  message?: string;
};

export type ApiErrorResponse = {
  message?: string;
  code?: string;
};

export type UserProfileDto = {
  id: string;
  primaryEmail: string;
  fullName: string;
  displayName: string;
  handle: string | null;
  profileImageUrl: string | null;
  profileSubtitle: string | null;
  timezone: string;
  locale: string;
  preferredCurrency: string;
  role: string;
  emailVerified: boolean;
  onboardingStatus: string;
  twoFactorEnabled: boolean;
  planTier: string;
  createdUtc: string;
  lastLoginUtc: string | null;
};

export type AuthTokenResponse = {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
  sessionId: string;
  user: UserProfileDto;
  mfaTrustedDevice?: MfaTrustedDeviceCredentialResponse | null;
};

export type MfaTrustedDeviceCredentialResponse = {
  token: string;
  expiresUtc: string;
};

export type CodeDeliveryResponse = {
  challengeId: string;
  expiresUtc: string;
  resendAfterSeconds: number;
  message: string;
};

export type MfaLoginChallengeResponse = {
  challengeId: string;
  challengeToken: string;
  expiresUtc: string;
  methods: Array<"totp" | "recovery_code">;
  accountHint: string;
};

export type AuthFlowResponse = {
  status: "authenticated" | "mfa_required" | "email_verification_required";
  session: AuthTokenResponse | null;
  mfaChallenge: MfaLoginChallengeResponse | null;
  emailVerification: CodeDeliveryResponse | null;
};

export type RegistrationResponse = CodeDeliveryResponse & {
  status: "email_verification_required";
};

export type DeviceContextDto = {
  deviceFingerprint?: string | null;
  deviceLabel?: string | null;
  platform?: string | null;
  osVersion?: string | null;
  appVersion?: string | null;
};

export type RegisterRequest = {
  email: string;
  password: string;
  displayName?: string | null;
  timezone?: string | null;
  locale?: string | null;
  preferredCurrency?: string | null;
  captchaToken?: string | null;
  deviceContext?: DeviceContextDto | null;
  acceptPolicies: boolean;
  termsVersion: string;
  privacyVersion: string;
};

export type LoginRequest = {
  email: string;
  password: string;
  captchaToken?: string | null;
  deviceContext?: DeviceContextDto | null;
  mfaTrustedDeviceToken?: string | null;
};

export type GoogleLoginRequest = {
  idToken: string;
  deviceContext?: DeviceContextDto | null;
  acceptPolicies?: boolean;
  termsVersion?: string | null;
  privacyVersion?: string | null;
  mfaTrustedDeviceToken?: string | null;
};

export type MicrosoftLoginRequest = {
  accessToken: string;
  deviceContext?: DeviceContextDto | null;
  acceptPolicies?: boolean;
  termsVersion?: string | null;
  privacyVersion?: string | null;
  mfaTrustedDeviceToken?: string | null;
};

export type RefreshTokenRequest = {
  refreshToken: string;
  deviceContext?: DeviceContextDto | null;
};

export type SessionDto = {
  id: string;
  createdUtc: string;
  expiresUtc: string;
  lastSeenUtc: string;
  revokedUtc: string | null;
  deviceLabel: string;
  platform: string | null;
  osVersion: string | null;
  appVersion: string | null;
  isCurrentSession: boolean;
};

export type AuthActionResponse = {
  message: string;
};

export type ForgotPasswordRequest = {
  identity: string;
};

export type VerifyPasswordRecoveryCodeRequest = {
  challengeId: string;
  code: string;
};

export type PasswordRecoveryGrantResponse = {
  challengeId: string;
  recoveryToken: string;
  expiresUtc: string;
};

export type ResetPasswordRequest = {
  challengeId: string;
  recoveryToken: string;
  newPassword: string;
};

export type RequestEmailVerificationRequest = {
  email: string;
};

export type ConfirmEmailVerificationRequest = {
  challengeId: string;
  code: string;
  deviceContext?: DeviceContextDto | null;
};

export type ChangePasswordRequest = {
  currentPassword: string;
  newPassword: string;
};

export type VerifyPasswordChangeCodeRequest = {
  challengeId: string;
  code: string;
};

export type ConfirmPasswordChangeCodeRequest = {
  challengeId: string;
  grantToken: string;
  newPassword: string;
};

export type MicrosoftAuthOptionsDto = {
  isConfigured: boolean;
  clientId: string | null;
  authority: string;
  scope: string | null;
};

export type MfaStatusResponse = {
  enabled: boolean;
  method: "totp" | null;
  recoveryCodesRemaining: number;
};

export type BeginTotpEnrollmentResponse = {
  authenticatorId: string;
  secret: string;
  otpAuthUri: string;
  expiresUtc: string;
};

export type ConfirmTotpEnrollmentRequest = {
  authenticatorId: string;
  code: string;
};

export type ConfirmTotpEnrollmentResponse = {
  enabled: boolean;
  recoveryCodes: string[];
};

export type VerifyMfaLoginRequest = {
  challengeId: string;
  challengeToken: string;
  code: string;
  method: "totp" | "recovery_code";
  deviceContext?: DeviceContextDto | null;
  rememberDevice?: boolean;
};

export type VerifyRememberedSessionMfaRequest = VerifyMfaLoginRequest & {
  refreshToken: string;
};

export type ResumeRememberedSessionWithTrustedDeviceRequest = {
  refreshToken: string;
  trustedDeviceToken: string;
  deviceContext?: DeviceContextDto | null;
};

export type DisableMfaRequest = {
  code: string;
  method: "totp" | "recovery_code";
};

export type PasswordPolicyCheckRequest = {
  password: string;
};

export type PasswordPolicyCheckResponse = {
  breachStatus: "safe" | "compromised" | "unavailable";
  minLength: number;
  maxLength: number;
  hasNumberOrSymbol: boolean;
  isLengthValid: boolean;
};

export type UserProfileDetailsDto = {
  id: string;
  primaryEmail: string;
  fullName: string;
  displayName: string;
  handle: string | null;
  profileImageUrl: string | null;
  profileSubtitle: string | null;
  timezone: string;
  locale: string;
  preferredCurrency: string;
  onboardingStatus: string;
  twoFactorEnabled: boolean;
  phoneNumber: string | null;
  dateOfBirth: string | null;
  countryRegion: string | null;
  financialFocus: string[];
  employmentStatus: string | null;
  incomeStability: string | null;
  primaryFinancialConcern: string | null;
  emailVerified: boolean;
  planTier: string;
  createdUtc: string;
  lastLoginUtc: string | null;
};

export type UpdateUserProfileRequest = {
  fullName: string;
  displayName: string;
  handle?: string | null;
  profileImageUrl?: string | null;
  profileSubtitle?: string | null;
  timezone: string;
  locale: string;
  preferredCurrency: string;
  onboardingStatus: string;
  dateOfBirth?: string | null;
  countryRegion?: string | null;
  financialFocus?: string[];
  employmentStatus?: string | null;
  incomeStability?: string | null;
  primaryFinancialConcern?: string | null;
};

export type UserPreferenceDto = {
  adviceTonePreference: string;
  digestFrequency: string;
  reminderPreference: string;
  notificationPreferencesJson: string;
  privacyPreferencesJson: string;
  essentialCategoryPreferencesJson: string;
  futureGoalConfigurationJson: string;
  updatedUtc: string;
};

export type UpdateUserPreferenceRequest = {
  adviceTonePreference: string;
  digestFrequency: string;
  reminderPreference: string;
  notificationPreferencesJson: string;
  privacyPreferencesJson: string;
  essentialCategoryPreferencesJson: string;
  futureGoalConfigurationJson: string;
};

export type PolicyVersionDto = {
  policyType: string;
  policyName: string;
  version: string;
  effectiveUtc: string;
  contentReference: string;
  contentMarkdown: string;
  isActive: boolean;
};

export type PolicyAcceptanceDto = {
  policyType: string;
  policyVersion: string;
  acceptedUtc: string;
  acceptanceContext: string;
  platform: string | null;
  appVersion: string | null;
};

export type AcceptPolicyRequest = {
  policyType: string;
  policyVersion: string;
  acceptanceContext: string;
  platform?: string | null;
  appVersion?: string | null;
};

export type ConsentRecordDto = {
  consentType: string;
  status: string;
  updatedUtc: string;
  grantedUtc: string | null;
  revokedUtc: string | null;
  source: string;
  metadataJson: string | null;
};

export type UpdateConsentRequest = {
  consentType: string;
  status: string;
  source: string;
  metadataJson?: string | null;
};

export type SupportRequestDto = {
  id: string;
  userId: string | null;
  category: string;
  subcategory: string;
  title: string;
  message: string;
  contactEmail: string | null;
  screenshotReference: string | null;
  connectionId: string | null;
  linkedBankAccountId: string | null;
  diagnosticsJson: string;
  status: string;
  createdUtc: string;
  updatedUtc: string;
};

export type CreateSupportRequestRequest = {
  category: string;
  subcategory: string;
  title: string;
  message: string;
  contactEmail?: string | null;
  connectionId?: string | null;
  linkedBankAccountId?: string | null;
  screenshots?: SupportScreenshotUploadRequest[] | null;
};

export type SupportScreenshotUploadRequest = {
  fileName: string;
  contentType: string;
  base64Data: string;
};

export type CreateDeletionRequestRequest = {
  verificationCode: string;
  notes?: string | null;
};

export type DeletionRequestDto = {
  id: string;
  userId: string;
  status: string;
  requestedUtc: string;
  updatedUtc: string;
  notes: string | null;
};

export type CreateExportRequestRequest = {
  notes?: string | null;
  format?: "xlsx" | "csv" | null;
  connectionId?: string | null;
  financialAccountId?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  periodPreset?: string | null;
};

export type ExportRequestDto = {
  id: string;
  userId: string;
  status: string;
  requestedUtc: string;
  updatedUtc: string;
  notes: string | null;
  format: string;
  connectionId: string | null;
  connectionLabel: string | null;
  financialAccountId: string | null;
  startDate: string | null;
  endDate: string | null;
  periodPreset: string | null;
  fileSizeBytes: number | null;
};

export type GoogleAuthOptionsDto = {
  isConfigured: boolean;
  providerType: string;
  authorizationUrl: string | null;
  callbackPath: string | null;
  message: string;
};

export type BankConnectionStatus =
  | "not_connected"
  | "connection_started"
  | "consent_in_progress"
  | "connected_pending_sync"
  | "connected"
  | "sync_pending"
  | "synced"
  | "reauth_required"
  | "expired"
  | "disconnect_pending"
  | "disconnect_failed"
  | "revoked"
  | "failed";

export type BankConnectionDto = {
  id: string;
  provider: string;
  providerId: string | null;
  providerEnvironment: string;
  providerDisplayName: string | null;
  providerIconUrl: string | null;
  providerLogoUrl: string | null;
  providerBrandBgColor: string | null;
  brandingLastSyncedAtUtc: string | null;
  status: BankConnectionStatus;
  createdUtc: string;
  updatedUtc: string;
  lastSuccessfulSyncUtc: string | null;
  lastSyncAttemptedUtc: string | null;
  lastErrorCode: string | null;
  grantedScopesCsv: string | null;
  supportsInfo: boolean | null;
  supportsCards: boolean | null;
  supportsDirectDebits: boolean | null;
  supportsStandingOrders: boolean | null;
  connectedFullName: string | null;
  identityFetchedUtc: string | null;
  syncLifecyclePhase?:
    | "connecting"
    | "importing_bank_data"
    | "import_complete_enrichment_queued"
    | "organizing_transactions"
    | "completed"
    | "sync_taking_longer_than_expected"
    | "attention_required"
    | null;
  syncLifecycleReason?: string | null;
  syncEnrichmentStage?: string | null;
  linkedAccountCount?: number | null;
  importedTransactionCount?: number | null;
  syncStateReconciled?: boolean | null;
  syncStateStaleProtectionApplied?: boolean | null;
  historicalEnrichmentInProgress?: boolean | null;
  historicalEnrichmentCompleted?: boolean | null;
  historicalEnrichmentProgressPercent?: number | null;
  historicalEnrichmentCheckpointUtc?: string | null;
  connectionLifecycleStage?:
    | "idle"
    | "launching_authorization"
    | "awaiting_bank_authorization"
    | "authorization_returned"
    | "authorization_confirmed"
    | "deep_link_return_initiated"
    | "returned_to_app"
    | "connection_created"
    | "fetching_accounts"
    | "fetching_balances"
    | "fetching_transactions"
    | "transactions_fetched"
    | "categorization_pending"
    | "categorization_running"
    | "post_processing_running"
    | "completed"
    | "completed_with_limited_history"
    | "completed_with_warnings"
    | "delayed_retrying"
    | "cooldown_wait"
    | "provider_slow"
    | "partial_failure"
    | "failed"
    | "reauth_required"
    | "disconnected"
    | "disconnecting"
    | null;
  connectionLifecycleReason?: string | null;
  safeToLeave?: boolean | null;
  safeToClose?: boolean | null;
  backgroundContinuationGuaranteed?: boolean | null;
  userActionRequired?: boolean | null;
  userActionKind?: "none" | "reconnect" | "retry_sync" | "retry_disconnect" | null;
  completionSemantics?:
    | "in_progress"
    | "completed"
    | "completed_with_limited_history"
    | "completed_with_warnings"
    | "needs_attention"
    | null;
  lifecycleLastUpdatedUtc?: string | null;
};

export type ConnectedBanksOverviewDto = {
  activeConnections: BankConnectionDto[];
  attentionConnections: BankConnectionDto[];
};

export type StartTrueLayerLinkRequest = {
  appReturnUri?: string | null;
  connectionId?: string | null;
};

export type LinkedBankAccountDto = {
  id: string;
  connectionId: string;
  financialAccountId: string | null;
  providerAccountId: string;
  providerId: string | null;
  providerDisplayName: string | null;
  providerIconUrl: string | null;
  providerLogoUrl: string | null;
  providerBrandBgColor: string | null;
  displayName: string;
  accountType: string | null;
  accountSubType: string | null;
  currency: string;
  currentConnectionHealth: string;
  latestAvailable: number | null;
  latestCurrent: number | null;
  latestOverdraft: number | null;
  createdUtc: string;
  updatedUtc: string;
  accountNumberMetadataJson: string | null;
};

export type LinkedBankCardDto = {
  id: string;
  connectionId: string;
  providerCardId: string;
  providerAccountId: string | null;
  displayName: string;
  currency: string;
  cardType: string | null;
  cardNetwork: string | null;
  cardNumberLastFour: string | null;
  nameOnCard: string | null;
  validFromUtc: string | null;
  validToUtc: string | null;
  currentConnectionHealth: string;
  latestAvailable: number | null;
  latestCurrent: number | null;
  latestLimit: number | null;
  latestOutstanding: number | null;
  createdUtc: string;
  updatedUtc: string;
};

export type BankDirectDebitDto = {
  id: string;
  linkedBankAccountId: string;
  connectionId: string;
  accountDisplayName: string;
  providerDirectDebitId: string;
  status: string | null;
  mandateType: string | null;
  reference: string | null;
  merchantName: string | null;
  previousPaymentDateUtc: string | null;
  previousPaymentAmount: number | null;
  previousPaymentCurrency: string | null;
  nextPaymentDateUtc: string | null;
  nextPaymentAmount: number | null;
  nextPaymentCurrency: string | null;
  updatedUtc: string;
};

export type BankStandingOrderDto = {
  id: string;
  linkedBankAccountId: string;
  connectionId: string;
  accountDisplayName: string;
  providerStandingOrderId: string;
  status: string | null;
  frequency: string | null;
  reference: string | null;
  payeeName: string | null;
  firstPaymentDateUtc: string | null;
  nextPaymentDateUtc: string | null;
  finalPaymentDateUtc: string | null;
  nextPaymentAmount: number | null;
  nextPaymentCurrency: string | null;
  updatedUtc: string;
};

export type BankRecurringPaymentsDto = {
  directDebits: BankDirectDebitDto[];
  standingOrders: BankStandingOrderDto[];
};

export type StartTrueLayerLinkResponse = {
  connectionId: string;
  attemptId: string;
  provider: string;
  environment: string;
  authorizationUrl: string;
  scopes: string[];
  expiresAtUtc: string;
};

export type BankConnectionAttemptStatusDto = {
  attemptId: string;
  connectionId: string;
  status: string;
  safeToClose: boolean;
  shouldAutoClose: boolean;
  shouldAutoReturn: boolean;
  manualActionRequired: boolean;
  headline: string;
  message: string;
  updatedUtc: string;
  expiresUtc: string;
  callbackHandledUtc: string | null;
  appReturnInitiatedUtc: string | null;
  appReturnConfirmedUtc: string | null;
  completedUtc: string | null;
};

export type SyncConnectionResponse = {
  connectionId: string;
  accountsSynced: number;
  balancesSynced: number;
  transactionsImported: number;
  status: BankConnectionStatus;
  syncedAtUtc: string;
  dataChanged: boolean;
};

export type GlobalBankSyncTrigger = "manual" | "auto";

export type GlobalBankSyncRequest = {
  trigger?: GlobalBankSyncTrigger | null;
  source?: string | null;
  force?: boolean | null;
};

export type GlobalBankSyncConnectionResponse = {
  connectionId: string;
  providerDisplayName: string | null;
  status: BankConnectionStatus;
  outcome:
    | "completed_changed"
    | "completed_no_change"
    | "failed"
    | "skipped_ineligible_status"
    | "skipped_unavailable"
    | "skipped_sync_in_progress"
    | "skipped_provider_backoff";
  accountsSynced: number;
  balancesSynced: number;
  transactionsImported: number;
  syncedAtUtc: string | null;
  dataChanged: boolean;
  lastSyncAttemptedUtc: string | null;
  lastSuccessfulSyncUtc: string | null;
  providerBackoffUntilUtc: string | null;
  latestFetchedRowUtc: string | null;
  hasFetchedRowNewerThanCheckpoint: boolean | null;
  freshnessSummary: string | null;
  historicalEnrichmentInProgress?: boolean | null;
  historicalEnrichmentCompleted?: boolean | null;
  historicalEnrichmentProgressPercent?: number | null;
  historicalEnrichmentCheckpointUtc?: string | null;
  errorCode: string | null;
  errorMessage: string | null;
};

export type GlobalBankSyncResponse = {
  trigger: GlobalBankSyncTrigger;
  outcome:
    | "completed"
    | "skipped_cooldown"
    | "skipped_not_due"
    | "skipped_provider_backoff"
    | "skipped_no_eligible_connections"
    | "failed_unexpected";
  requestedAtUtc: string;
  completedAtUtc: string | null;
  dueNow: boolean;
  cooldownRemainingSeconds: number;
  cooldownUntilUtc: string | null;
  eligibleConnectionCount: number;
  changedConnectionCount: number;
  noChangeConnectionCount: number;
  failedConnectionCount: number;
  skippedConnectionCount: number;
  lastSuccessfulSyncUtc: string | null;
  lastManualSyncRequestUtc: string | null;
  nextEligibleManualSyncUtc: string | null;
  providerBackoffConnectionCount: number;
  noNewerRowsConnectionCount: number;
  connections: GlobalBankSyncConnectionResponse[];
};

export type BankEnrichmentConnectionProgressDto = {
  connectionId: string;
  providerDisplayName: string | null;
  inProgress: boolean;
  completed: boolean;
  progressPercent: number;
  processedCount: number;
  totalCount: number;
  remainingCount: number;
  stage: string;
  lastUpdatedUtc: string | null;
};

export type BankEnrichmentProgressDto = {
  inProgress: boolean;
  completed: boolean;
  progressPercent: number;
  processedCount: number;
  totalCount: number;
  remainingCount: number;
  stage: string;
  lastUpdatedUtc: string | null;
  newestFirst: boolean;
  connections: BankEnrichmentConnectionProgressDto[];
};

export type SendAIChatMessageRequest = {
  message: string;
  clientRequestId: string;
  conversationThreadId?: string | null;
  requirePersistentMemory?: boolean;
  allowFallbackOnPersistentFailure?: boolean;
  state?: {
    activeTopic?: string | null;
    userIntent?: string | null;
    constraints?: Record<string, string> | null;
    summaries?: string[] | null;
    budgetPreference?: string | null;
    locationPreference?: string | null;
    merchantInvestigationSubject?: string | null;
    recentConclusions?: string[] | null;
  } | null;
  recentTurns?: {
    role: "system" | "developer" | "user" | "assistant";
    content: string;
    timestampUtc?: string | null;
    topic?: string | null;
    isResolved: boolean;
  }[] | null;
  metadata?: Record<string, string> | null;
  correlationId?: string | null;
};

export type SendAIChatMessageResponse = {
  conversationThreadId: string | null;
  turnId: string | null;
  status: string;
  message: string;
  modelUsed: string;
  reasoningClass: string;
  succeeded: boolean;
  deduped: boolean;
  inProgress: boolean;
  fallbackUsed: boolean;
  failureCode: string | null;
  failureReason: string | null;
  suggestedStateUpdates: Record<string, string>;
  warnings: string[];
  followUpIntentHints: string[];
  contextSummary: string | null;
  structuredResults?: CompanionStructuredResults | null;
};

export type ArchiveAIChatThreadResponse = {
  threadId: string;
  status: string;
};

export type AIChatThreadSummary = {
  threadId: string;
  title: string | null;
  status: string;
  startedUtc: string;
  lastMessageUtc: string;
  lastContextRefreshUtc: string | null;
  activeSummaryVersion: number;
};

export type AIChatMessage = {
  messageId: string;
  turnId: string | null;
  role: string;
  content: string;
  messageOrder: number;
  topic: string | null;
  modelUsed: string | null;
  taskType: string | null;
  isResolved: boolean;
  createdUtc: string;
};

export type AIChatThreadDetail = {
  thread: AIChatThreadSummary;
  messages: AIChatMessage[];
};

export type CompanionStructuredResults = {
  type: "places" | string;
  items: CompanionPlaceCardResult[];
};

export type CompanionPlaceCardResult = {
  id: string;
  name: string;
  distanceMeters?: number | null;
  photoUrl?: string | null;
  photoUrls?: string[] | null;
  formattedAddress?: string | null;
  shortFormattedAddress?: string | null;
  rating?: number | null;
  openNow?: boolean | null;
  priceLevel?: number | string | null;
  websiteUrl?: string | null;
  category?: string | null;
  primaryTypeDisplayName?: string | null;
  closesInMinutes?: number | null;
  opensInMinutes?: number | null;
  phoneNumber?: string | null;
  menuUrl?: string | null;
  googleMapsUri?: string | null;
  latitude?: number | null;
  longitude?: number | null;
};


