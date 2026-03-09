const fallbackInstagramUrl = "https://instagram.com/nsfintech";
const fallbackWebsiteUrl = "https://nsfintech.app";

export const externalLinks = {
  instagram: process.env.EXPO_PUBLIC_NSFINTECH_INSTAGRAM_URL?.trim() || fallbackInstagramUrl,
  website: process.env.EXPO_PUBLIC_NSFINTECH_WEBSITE_URL?.trim() || fallbackWebsiteUrl
} as const;

