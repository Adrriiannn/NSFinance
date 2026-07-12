const { createRunOncePlugin, withProjectBuildGradle } = require("expo/config-plugins");

const MICROSOFT_MAVEN_REPOSITORY =
  "maven { url 'https://pkgs.dev.azure.com/MicrosoftDeviceSDK/DuoSDK-Public/_packaging/Duo-SDK-Feed/maven/v1' }";
const ALL_PROJECTS_REPOSITORY_ANCHOR = `allprojects {
  repositories {
    google()
    mavenCentral()`;

function withMicrosoftMavenRepository(config) {
  return withProjectBuildGradle(config, (configWithBuildGradle) => {
    if (configWithBuildGradle.modResults.language !== "groovy") {
      return configWithBuildGradle;
    }

    const currentContents = configWithBuildGradle.modResults.contents;
    if (currentContents.includes(MICROSOFT_MAVEN_REPOSITORY)) {
      return configWithBuildGradle;
    }

    if (!currentContents.includes(ALL_PROJECTS_REPOSITORY_ANCHOR)) {
      throw new Error("Could not locate the Android allprojects repository block for MSAL.");
    }

    configWithBuildGradle.modResults.contents = currentContents.replace(
      ALL_PROJECTS_REPOSITORY_ANCHOR,
      `${ALL_PROJECTS_REPOSITORY_ANCHOR}\n    ${MICROSOFT_MAVEN_REPOSITORY}`
    );

    return configWithBuildGradle;
  });
}

module.exports = createRunOncePlugin(
  withMicrosoftMavenRepository,
  "with-microsoft-maven-repository",
  "1.0.0"
);
