using Microsoft.EntityFrameworkCore;
using NSFinance.Api.Persistence;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Infrastructure.Seeding;

public sealed class PolicyDataSeeder
{
    public async Task SeedAsync(AppDbContext dbContext, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var policies = new (string PolicyType, string Name, string Version, string ContentRef)[]
        {
            ("terms_of_service", "Terms of Service", "1.0.0", "legal/terms/v1"),
            ("privacy_policy", "Privacy Policy", "1.0.0", "legal/privacy/v1"),
            ("ai_limitations_notice", "AI Limitations Notice", "1.0.0", "legal/ai-limitations/v1"),
            ("open_banking_disclosure", "Open Banking Disclosure", "1.0.0", "legal/open-banking-disclosure/v1"),
            ("ai_disclosure", "AI Disclosure", "1.0.0", "legal/ai-disclosure/v1"),
            ("data_rights_gdpr_summary", "Data Rights / GDPR Summary", "1.0.0", "legal/data-rights-gdpr/v1"),
            ("marketing_communications", "Marketing Communications Consent", "1.0.0", "legal/marketing-consent/v1")
        };

        foreach (var (policyType, name, version, contentRef) in policies)
        {
            var document = await dbContext.PolicyDocuments
                .SingleOrDefaultAsync(x => x.PolicyType == policyType, cancellationToken);

            if (document is null)
            {
                document = new PolicyDocument
                {
                    Id = Guid.NewGuid(),
                    PolicyType = policyType,
                    Name = name,
                    CreatedUtc = now
                };
                dbContext.PolicyDocuments.Add(document);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var existingVersion = await dbContext.PolicyVersions
                .SingleOrDefaultAsync(
                    x => x.PolicyDocumentId == document.Id && x.Version == version,
                    cancellationToken);

            var contentMarkdown = GetPolicyContentMarkdown(policyType);

            if (existingVersion is null)
            {
                dbContext.PolicyVersions.Add(new PolicyVersion
                {
                    Id = Guid.NewGuid(),
                    PolicyDocumentId = document.Id,
                    Version = version,
                    EffectiveUtc = now,
                    ContentReference = contentRef,
                    ContentMarkdown = contentMarkdown,
                    IsActive = true,
                    CreatedUtc = now
                });
                continue;
            }

            existingVersion.ContentReference = contentRef;
            existingVersion.ContentMarkdown = contentMarkdown;
            existingVersion.IsActive = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string GetPolicyContentMarkdown(string policyType)
    {
        return policyType switch
        {
            "terms_of_service" =>
                """
                # Terms of Service (Draft)

                ## 1. Scope and Service Overview
                These Terms govern your use of NSFinance mobile and related backend services. NSFinance provides account views, linked-bank data ingestion, spending analysis, planning surfaces, and optional AI-assisted summaries.
                This draft is production-shaped and pending external legal review before public launch.

                ## 2. Eligibility and Account Registration
                You must be legally permitted to use the service in your jurisdiction and provide accurate registration information.
                You are responsible for keeping your profile and contact details up to date so security and service notices reach you.

                ## 3. Authentication and Account Security
                You must maintain confidentiality of your login credentials and protect access to your devices.
                You must notify NSFinance promptly if you suspect unauthorized access.
                Session/device controls are provided in Security settings, including terminating active sessions.

                ## 4. Open Banking Connectivity
                NSFinance connects to financial institutions through regulated Open Banking providers, including TrueLayer.
                NSFinance does not ask for or store your online banking credentials.
                Bank connections are consent-based and can expire; reconnection may be required (for example after provider/bank consent expiry windows).
                Data availability, freshness, and completeness may vary based on provider and bank uptime.

                ## 5. Nature of Financial Data Displayed
                Amounts, balances, and transactions shown in NSFinance may occasionally be delayed, incomplete, duplicated, or corrected by banks/providers.
                NSFinance may store imported snapshots for operational support, diagnostics, and audit purposes.
                You remain responsible for verifying critical account information directly with your bank where needed.

                ## 6. AI-Assisted Features and Limitations
                NSFinance may provide AI-generated summaries, trend explanations, and suggestions where enabled.
                AI output may be inaccurate, incomplete, or stale and is informational only.
                NSFinance does not provide financial, tax, legal, mortgage, or investment advice.
                You remain solely responsible for financial decisions and actions.

                ## 7. Acceptable Use
                You agree not to misuse the service, interfere with infrastructure, attempt unauthorized access, scrape data unlawfully, or use NSFinance for fraud, abuse, or illegal activity.

                ## 8. Support, Diagnostics, and Communications
                When you submit support requests, NSFinance may attach diagnostics relevant to troubleshooting, including device/session context and banking sync context where applicable.
                Operational and security communications may be provided through in-app notices and configured contact channels.

                ## 9. Availability and Third-Party Dependencies
                NSFinance may be unavailable due to maintenance, incidents, provider outages, bank downtime, or network conditions.
                We do not guarantee uninterrupted or error-free operation.

                ## 10. Disconnecting Banks and User Controls
                You can disconnect linked banks in Security settings.
                Disconnecting a bank removes related active linked-account data from normal user-facing views while preserving minimal required operational/audit records.

                ## 11. Data Export and Deletion Requests
                NSFinance provides in-app electronic requests for data export and account deletion.
                Request statuses and timestamps are shown in-app.
                Some records may be retained where required by law, security, anti-fraud, or operational integrity obligations.

                ## 12. Suspension, Restriction, and Termination
                NSFinance may suspend or restrict access for security risk, abuse, legal compliance, or suspected fraudulent activity.
                You may stop using the service at any time.

                ## 13. Limitation of Liability
                To the maximum extent permitted by law, NSFinance is not liable for indirect, incidental, special, or consequential damages arising from service use, third-party outages, provider interruptions, or data delays.

                ## 14. Governing Law and Jurisdiction
                Governing law and jurisdiction details will be published here before public launch.

                ## 15. Operator and Contact Details
                Operator details will be published here before public launch.
                - Legal entity name: pending publication
                - Registered address: pending publication
                - Support contact email: pending publication
                - Privacy contact email: pending publication
                """,
            "privacy_policy" =>
                """
                # Privacy Policy (Draft)

                ## 1. Controller and Operator Context
                NSFinance operates a finance application with account settings, security controls, Open Banking connectivity, support workflows, and optional AI-assisted insights.
                This policy is a production-shaped draft pending external legal review before public launch.

                ## 2. Categories of Personal Data We Process
                - Profile and account data: email, full name, display name, profile bio, phone, date of birth, country, timezone, preferred currency, and related preferences.
                - Security/session data: session IDs, device labels, platform/OS/app metadata, login events, and revocation events.
                - Financial/Open Banking data: linked bank connections, linked accounts, balances, imported transactions, sync timestamps, provider statuses, and related diagnostics.
                - Support data: issue category/subcategory, descriptions, optional screenshot attachments, and diagnostics metadata attached to requests.
                - Legal/consent data: consent statuses, policy acceptances, acceptance context and timestamps.
                - AI preference data: toggles and personalization settings controlling AI-related features.

                ## 3. Data Sources
                - Directly from you (registration, profile updates, support submissions, settings changes).
                - From regulated Open Banking providers and connected banks where you authorize access.
                - From operational telemetry needed for security and support (app/device/session context).

                ## 4. Purposes of Processing
                - Provide account authentication and secure session management.
                - Display and sync linked financial accounts, balances, and transactions.
                - Provide planning/spending features and optional AI-assisted summaries or suggestions where enabled.
                - Investigate incidents and resolve support requests.
                - Meet compliance, anti-fraud, security, and legal obligations.
                - Maintain service quality and reliability.

                ## 5. Legal Bases (GDPR-Style Overview)
                Depending on context, NSFinance may rely on:
                - Contract performance (providing core app functionality you request).
                - Legitimate interests (service security, diagnostics, fraud prevention, reliability).
                - Legal obligations (recordkeeping, lawful compliance responses).
                - Consent (for specific optional processing where required).

                ## 6. Open Banking-Specific Processing
                Open Banking access is consent-based and provided through regulated providers such as TrueLayer.
                NSFinance does not store your online banking credentials.
                Consent can expire and reconnection may be required.

                ## 7. AI and Personalization Processing
                If enabled, AI features may process transaction/balance context to generate summaries and suggestions.
                AI outputs are informational only and may be inaccurate or incomplete.
                You can manage related controls in Privacy settings.

                ## 8. Support Diagnostics and Operational Metadata
                Support requests can include diagnostics such as user/session identifiers, app version, platform/device metadata, linked connection context, account context, and sync status to speed troubleshooting.
                Where relevant, bank-specific identifiers may be included when you select context in the support form.

                ## 9. Recipients and Processor Categories
                - Open Banking providers (for consented bank data connectivity).
                - Cloud infrastructure providers (for hosting and backend operations).
                - Support tooling providers (for request handling workflows).
                - AI infrastructure providers where AI features are enabled.

                ## 10. International Data Transfers
                Data may be processed in multiple jurisdictions depending on infrastructure and service providers.
                Transfer safeguards and jurisdictional details will be published here before public launch.

                ## 11. Retention and Deletion Principles
                Retention periods vary by data type, operational need, security, and legal obligations.
                Account deletion requests deactivate active access and trigger controlled cleanup workflows.
                Minimal records may be retained where legally or operationally required.
                Deletion requests apply to NSFinance app-owned records and artifacts; third-party processor retention may follow their own legal obligations and controls.

                ## 12. Your Rights
                Subject to applicable law, you may request:
                - Access to personal data.
                - Rectification of inaccurate data.
                - Erasure/deletion.
                - Restriction of processing in applicable cases.
                - Data portability/export.
                - Objection to certain processing where applicable.
                - Complaint to a relevant supervisory authority.

                ## 13. Rights Request Process and Timing
                Rights requests can be submitted electronically in-app (for example export/deletion) and through support/privacy contact channels.
                NSFinance aims to respond within standard legal windows, typically within one month unless extended as permitted by law.

                ## 14. Security Measures
                NSFinance applies technical and organizational controls proportionate to risk, including authenticated access controls, transport security, encryption for sensitive tokens, audit logging, and session management controls.

                ## 15. Operator and Privacy Contact Details
                Operator details will be published here before public launch.
                - Legal entity name: pending publication
                - Registered address: pending publication
                - Privacy contact email: pending publication
                - General support email: pending publication
                """,
            "open_banking_disclosure" =>
                """
                # Open Banking Disclosure (Draft)

                ## 1. What Open Banking Means in NSFinance
                Open Banking allows you to connect eligible bank accounts to NSFinance through regulated providers, including TrueLayer.
                Connection represents your explicit consent/authentication relationship for account data access.

                ## 2. Credentials and Security
                NSFinance does not ask for or store your online banking credentials.
                Authentication and consent are handled through regulated provider and bank flows.

                ## 3. Data Categories Accessed
                Depending on granted permissions, NSFinance may access:
                - Account metadata
                - Account balances
                - Transaction history
                - Connection/sync status information

                ## 4. Consent and Expiry
                Open Banking access is consent-based and time-limited by provider/bank rules.
                Data-access consent may expire (commonly within up to 90 days in some environments), so reconnection may be required to continue syncing.

                ## 5. Disconnecting Access
                You can disconnect linked banks from Security settings in NSFinance.
                Disconnecting removes related linked accounts from active user-facing views while preserving minimal required audit/operational records.

                ## 6. Data Availability Caveats
                Data timeliness and completeness depend on bank and provider uptime.
                You may occasionally see delayed or incomplete data due to upstream provider/bank conditions.
                """,
            "ai_disclosure" =>
                """
                # AI Disclosure (Draft)

                ## 1. What AI Features May Do
                NSFinance may provide AI-assisted summaries, trend explanations, and spending/planning suggestions where those features are enabled.

                ## 2. Optional and Configurable Behavior
                AI-related controls are available in Privacy settings, including analysis/summaries/suggestions toggles.
                You can disable supported AI personalization settings at any time.

                ## 3. Accuracy and Limitations
                AI outputs can be inaccurate, incomplete, or context-limited.
                AI may miss relevant context, especially during data delays, sparse history, or unusual one-off financial events.

                ## 4. No Advice and Decision Responsibility
                AI outputs are informational only and are not financial, tax, investment, legal, or mortgage advice.
                You must independently verify important decisions and remain responsible for actions taken.

                ## 5. Automation Scope
                NSFinance does not make automated financial decisions on your behalf in current scope.
                If this changes in future releases, NSFinance will provide explicit disclosure before rollout.
                """,
            "data_rights_gdpr_summary" =>
                """
                # Data Rights / GDPR Summary (Draft)

                ## 1. Your Core Rights
                Subject to applicable law, you can request:
                - Access to your personal data
                - Correction/rectification of inaccurate information
                - Data export/portability
                - Deletion/erasure
                - Restriction or objection in applicable cases

                ## 2. In-App Electronic Requests
                NSFinance supports electronic rights handling where data is processed electronically.
                In Security and Privacy surfaces, you can submit export/deletion requests and view request statuses/timestamps.

                ## 3. Export Requests
                Export requests generate a machine-readable package containing key account/profile/financial/support/legal preference records available in current scope.
                Export status is visible in-app and download is provided when the package is ready.

                ## 4. Deletion Requests
                Deletion requests require an additional verification step.
                Once accepted, active access is deactivated and cleanup workflows are initiated, while minimal lawful/operational records may be retained.

                ## 5. Timing Expectations
                NSFinance aims to respond to rights requests within standard legal timelines, generally within one month unless an extension is permitted by law.

                ## 6. Questions, Complaints, and Escalation
                You can submit privacy/data-rights questions via Support.
                Supervisory authority complaint paths and final operator contact details will be published here before public launch.
                """,
            "ai_limitations_notice" =>
                """
                # AI Limitations Notice (Draft)

                AI models can misclassify or misunderstand financial activity.
                Suggestions are probabilistic and may not reflect your complete context.

                Always verify transactions, balances, and planning assumptions before acting.
                """,
            "marketing_communications" =>
                """
                # Marketing Communications Consent

                This document tracks whether you opt in to marketing updates, release notes, and product announcements.
                You can update this consent at any time in settings.
                """,
            _ => "Policy content pending."
        };
    }
}
