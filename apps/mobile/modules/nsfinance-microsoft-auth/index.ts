import { requireNativeModule } from "expo-modules-core";

export type MicrosoftNativeSignInResult =
  | {
      status: "success";
      accessToken: string;
    }
  | {
      status: "cancelled";
    };

type NsfinanceMicrosoftAuthModule = {
  signIn(scope: string): Promise<MicrosoftNativeSignInResult>;
};

export default requireNativeModule<NsfinanceMicrosoftAuthModule>("NsfinanceMicrosoftAuth");
