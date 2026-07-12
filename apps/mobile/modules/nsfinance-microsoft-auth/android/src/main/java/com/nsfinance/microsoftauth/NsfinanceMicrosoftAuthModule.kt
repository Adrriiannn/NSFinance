package com.nsfinance.microsoftauth

import com.microsoft.identity.client.AcquireTokenParameters
import com.microsoft.identity.client.AuthenticationCallback
import com.microsoft.identity.client.IAuthenticationResult
import com.microsoft.identity.client.IMultipleAccountPublicClientApplication
import com.microsoft.identity.client.IPublicClientApplication
import com.microsoft.identity.client.Prompt
import com.microsoft.identity.client.PublicClientApplication
import com.microsoft.identity.client.exception.MsalException
import expo.modules.kotlin.Promise
import expo.modules.kotlin.modules.Module
import expo.modules.kotlin.modules.ModuleDefinition

class NsfinanceMicrosoftAuthModule : Module() {
  @Volatile
  private var publicClientApplication: IMultipleAccountPublicClientApplication? = null

  override fun definition() = ModuleDefinition {
    Name("NsfinanceMicrosoftAuth")

    AsyncFunction("signIn") { scope: String, promise: Promise ->
      val context = appContext.reactContext
      val activity = appContext.currentActivity
      if (context == null || activity == null) {
        promise.reject(
          "microsoft_activity_unavailable",
          "Microsoft sign-in requires the active NSFinance screen.",
          null
        )
        return@AsyncFunction
      }

      if (scope.isBlank()) {
        promise.reject("microsoft_scope_missing", "Microsoft sign-in is not configured.", null)
        return@AsyncFunction
      }

      withApplication(
        onReady = { application ->
          val parameters = AcquireTokenParameters.Builder()
            .withScopes(listOf(scope))
            .startAuthorizationFromActivity(activity)
            .withPrompt(Prompt.SELECT_ACCOUNT)
            .withCallback(object : AuthenticationCallback {
              override fun onSuccess(authenticationResult: IAuthenticationResult) {
                promise.resolve(
                  mapOf(
                    "status" to "success",
                    "accessToken" to authenticationResult.accessToken
                  )
                )
              }

              override fun onError(exception: MsalException) {
                promise.reject(
                  "microsoft_sign_in_failed",
                  "Microsoft sign-in could not be completed.",
                  null
                )
              }

              override fun onCancel() {
                promise.resolve(mapOf("status" to "cancelled"))
              }
            })
            .build()

          application.acquireToken(parameters)
        },
        onError = { exception ->
          promise.reject(
            "microsoft_initialization_failed",
            "Microsoft sign-in could not start.",
            null
          )
        }
      )
    }
  }

  private fun withApplication(
    onReady: (IMultipleAccountPublicClientApplication) -> Unit,
    onError: (MsalException) -> Unit
  ) {
    publicClientApplication?.let(onReady) ?: run {
      val context = appContext.reactContext ?: return
      PublicClientApplication.createMultipleAccountPublicClientApplication(
        context,
        R.raw.nsfinance_msal_config,
        object : IPublicClientApplication.IMultipleAccountApplicationCreatedListener {
          override fun onCreated(application: IMultipleAccountPublicClientApplication) {
            publicClientApplication = application
            onReady(application)
          }

          override fun onError(exception: MsalException) {
            onError(exception)
          }
        }
      )
    }
  }
}
