import type { Metadata } from "next";
import { Suspense } from "react";

import { SurveyView } from "./survey-view";

export const metadata: Metadata = {
  title: { absolute: "Ziyaretinizi puanlayın" },
  robots: { index: false, follow: false },
  referrer: "no-referrer",
};

/** Memnuniyet anketi (SMS'teki kısa link): 1-5 puan, 1-3'te yorum, Google yorum linki. Oturum gerekmez. */
export default function SurveyPage() {
  return (
    <Suspense>
      <SurveyView />
    </Suspense>
  );
}
