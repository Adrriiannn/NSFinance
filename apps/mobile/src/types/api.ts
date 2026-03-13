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
  bookedAtUtc: string;
  createdUtc: string;
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
  deviceContext?: DeviceContextDto | null;
};

export type LoginRequest = {
  email: string;
  password: string;
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
};

export type ExportRequestDto = {
  id: string;
  userId: string;
  status: string;
  requestedUtc: string;
  updatedUtc: string;
  notes: string | null;
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
  | "revoked"
  | "failed";

export type BankConnectionDto = {
  id: string;
  provider: string;
  providerEnvironment: string;
  providerDisplayName: string | null;
  status: BankConnectionStatus;
  createdUtc: string;
  updatedUtc: string;
  lastSuccessfulSyncUtc: string | null;
  lastSyncAttemptedUtc: string | null;
  lastErrorCode: string | null;
};

export type ConnectedBanksOverviewDto = {
  activeConnections: BankConnectionDto[];
  attentionConnections: BankConnectionDto[];
};

export type LinkedBankAccountDto = {
  id: string;
  connectionId: string;
  providerAccountId: string;
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
};


