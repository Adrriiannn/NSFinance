# Auth Module

Implements Phase 1 identity/session/security foundation:

- register/login/refresh/logout
- logout all + session listing/revocation
- password reset + email verification token flows
- password change
- login abuse lockout support
- Google OIDC scaffold endpoints

Critical state is server-owned via `Sessions`, `SessionRefreshTokens`, `EmailActionTokens`, and audit events.
