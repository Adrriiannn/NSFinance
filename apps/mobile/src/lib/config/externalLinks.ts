const fallbackWebsiteUrl = "https://nsireland.ie";

export const externalLinks = {
  instagram: null,
  website: process.env.EXPO_PUBLIC_NSFINTECH_WEBSITE_URL?.trim() || fallbackWebsiteUrl
} as const;
