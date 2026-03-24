const { createRunOncePlugin, withSettingsGradle } = require("expo/config-plugins");

const LEGACY_AUTOLINK_CALL = "ex.autolinkLibrariesFromCommand()";
const EXPO_AUTOLINK_CALL = "ex.autolinkLibrariesFromCommand(expoAutolinking.rnConfigCommand)";

function withExpoAutolinkingCommand(config) {
  return withSettingsGradle(config, (configWithSettingsGradle) => {
    if (configWithSettingsGradle.modResults.language !== "groovy") {
      return configWithSettingsGradle;
    }

    const currentContents = configWithSettingsGradle.modResults.contents;
    if (!currentContents.includes(LEGACY_AUTOLINK_CALL)) {
      return configWithSettingsGradle;
    }

    configWithSettingsGradle.modResults.contents = currentContents.replace(
      LEGACY_AUTOLINK_CALL,
      EXPO_AUTOLINK_CALL
    );

    return configWithSettingsGradle;
  });
}

module.exports = createRunOncePlugin(
  withExpoAutolinkingCommand,
  "with-expo-autolinking-command",
  "1.0.0"
);
