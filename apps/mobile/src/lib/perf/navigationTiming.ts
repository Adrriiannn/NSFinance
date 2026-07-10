type RouterLike = {
  push: (href: string) => void;
  replace: (href: string) => void;
  navigate?: (href: string) => void;
};

export function navigateWithProbe(
  router: RouterLike,
  href: string,
  _source: string,
  mode: "navigate" | "push" | "replace" = "navigate"
) {
  if (mode === "push") {
    router.push(href);
    return;
  }

  if (mode === "replace") {
    router.replace(href);
    return;
  }

  if (typeof router.navigate === "function") {
    router.navigate(href);
    return;
  }

  router.replace(href);
}
