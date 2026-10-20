import type { Metadata } from "next";

import { PlatformFeedback } from "@/components/platform/platform-feedback";

export const instant = false;

export const metadata: Metadata = { title: "Geri bildirim" };

export default function PlatformFeedbackPage() {
  return <PlatformFeedback />;
}
