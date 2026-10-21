import { act, fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { describe, expect, it } from "vitest";

import { MoneyField } from "@/components/finance/finance-dialogs";

import { NumberInput } from "./number-input";

function Controlled(props: React.ComponentProps<typeof NumberInput>) {
  const [value, setValue] = useState("");
  return <NumberInput aria-label="Alan" value={value} onChange={(e) => setValue(e.target.value)} {...props} />;
}

function Money() {
  const [value, setValue] = useState("");
  return <MoneyField label="Tutar" value={value} onChange={setValue} />;
}

function Registered() {
  const { register } = useForm({ defaultValues: { amount: "" } });
  return <NumberInput aria-label="Kayıtlı" {...register("amount")} />;
}

const setNativeValue = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;

/** Klavyeden harf harf yazma: her karakter imlecin sonuna eklenir ve tarayıcıdaki gibi `insertText` InputEvent'i gelir. */
function type(input: HTMLElement, text: string) {
  for (const ch of text) {
    const el = input as HTMLInputElement;
    act(() => {
      setNativeValue.call(el, el.value + ch);
      el.setSelectionRange(el.value.length, el.value.length);
      el.dispatchEvent(new InputEvent("input", { bubbles: true, inputType: "insertText", data: ch }));
    });
  }
}

/** Yapıştırma: bütün metin tek seferde. */
function paste(input: HTMLElement, text: string) {
  fireEvent.input(input, { target: { value: text }, inputType: "insertFromPaste" });
}

describe("NumberInput", () => {
  it("para alanı (MoneyField) harf kabul etmez, nokta virgül olur, telefonda sayısal klavye", () => {
    render(<Money />);
    const input = screen.getByLabelText("Tutar");
    expect(input).toHaveAttribute("inputmode", "decimal");
    type(input, "Ikln");
    expect(input).toHaveValue("");
    type(input, "12a.5x0.");
    expect(input).toHaveValue("12,50");
  });

  it("tam sayı alanı: sadece rakam, numeric klavye", () => {
    render(<Controlled kind="integer" />);
    const input = screen.getByLabelText("Alan");
    expect(input).toHaveAttribute("inputmode", "numeric");
    type(input, "1,4 gün");
    expect(input).toHaveValue("14");
  });

  it("yapıştırılan '1.500,00 TL' binlik ayırıcısız okunur", () => {
    render(<Controlled />);
    const input = screen.getByLabelText("Alan");
    paste(input, "1.500,00 TL");
    expect(input).toHaveValue("1500,00");
  });

  it("eksi izinli alanda başta eksi girilir", () => {
    render(<Controlled allowNegative />);
    const input = screen.getByLabelText("Alan");
    type(input, "-2.5");
    expect(input).toHaveValue("-2,5");
  });

  it("react-hook-form register ile de süzer", () => {
    render(<Registered />);
    const input = screen.getByLabelText("Kayıtlı");
    type(input, "abc7.25");
    expect(input).toHaveValue("7,25");
  });
});
