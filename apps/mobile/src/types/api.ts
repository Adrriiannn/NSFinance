export type AccountType = "Current" | "Savings" | "Credit" | "Cash" | "Other";
export type TransactionDirection = "Income" | "Expense";

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
  transferKind?: "manual_transfer" | "linked_internal_transfer" | null;
  linkedTransferTransactionId?: string | null;
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
  biometricUnlockEnabled: boolean;
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
};

export type LoginRequest = {
  email: string;
  password: string;
  captchaToken?: string | null;
  deviceContext?: DeviceContextDto | null;
};

export type GoogleLoginRequest = {
  idToken: string;
  deviceContext?: DeviceContextDto | null;
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
  debugToken?: string | null;
};

export type ForgotPasswordRequest = {
  email: string;
};

export type ResetPasswordRequest = {
  token: string;
  newPassword: string;
};

export type RequestEmailVerificationRequest = {
  email: string;
};

export type ConfirmEmailVerificationRequest = {
  token: string;
};

export type ChangePasswordRequest = {
  currentPassword: string;
  newPassword: string;
};

export type VerifyPasswordChangeCodeRequest = {
  code: string;
};

export type ConfirmPasswordChangeCodeRequest = {
  code: string;
  newPassword: string;
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
  biometricUnlockEnabled: boolean;
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
  primaryEmail: string;
  fullName: string;
  displayName: string;
  handle?: string | null;
  profileImageUrl?: string | null;
  profileSubtitle?: string | null;
  timezone: string;
  locale: string;
  preferredCurrency: string;
  onboardingStatus: string;
  biometricUnlockEnabled: boolean;
  twoFactorEnabled: boolean;
  phoneNumber?: string | null;
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
  provider: string;
  environment: string;
  authorizationUrl: string;
  scopes: string[];
  expiresAtUtc: string;
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
};

export type GlobalBankSyncConnectionResponse = {
  connectionId: string;
  providerDisplayName: string | null;
  status: BankConnectionStatus;
  outcome:
    | "completed_changed"
    | "completed_no_change"
    | "failed"
    | "skipped_unavailable"
    | "skipped_sync_in_progress";
  accountsSynced: number;
  balancesSynced: number;
  transactionsImported: number;
  syncedAtUtc: string | null;
  dataChanged: boolean;
  errorCode: string | null;
  errorMessage: string | null;
};

export type GlobalBankSyncResponse = {
  trigger: GlobalBankSyncTrigger;
  outcome:
    | "completed"
    | "skipped_cooldown"
    | "skipped_not_due"
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
  connections: GlobalBankSyncConnectionResponse[];
};


