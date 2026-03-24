const path = require("path");

function resolvePackageRoot(packageName) {
  return path.dirname(require.resolve(`${packageName}/package.json`));
}

function androidDependencyConfig(packageName) {
  const root = resolvePackageRoot(packageName);

  return {
    root,
    platforms: {
      android: {
        sourceDir: "android"
      }
    }
  };
}

module.exports = {
  dependencies: {
    "react-native-safe-area-context": androidDependencyConfig("react-native-safe-area-context"),
    "react-native-screens": androidDependencyConfig("react-native-screens"),
    "react-native-svg": androidDependencyConfig("react-native-svg"),
    "react-native-webview": androidDependencyConfig("react-native-webview")
  }
};
