const assetExtensions = [".gif", ".jpeg", ".jpg", ".png", ".webp"];

for (const extension of assetExtensions) {
  require.extensions[extension] = (module, filename) => {
    module.exports = { uri: filename };
  };
}
