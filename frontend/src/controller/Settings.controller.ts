import type { Button$PressEvent } from "sap/m/Button";
import MessageBox from "sap/m/MessageBox";
import Context from "sap/ui/model/odata/v4/Context";
import * as api from "../model/api";
import type { SettingsDraft, SettingsView } from "../model/state";
import BaseController from "./Base.controller";

/** The /Settings singleton, as the EDM serialises it. */
interface SettingsRow {
  StampWord: string;
  EndingText: string;
  Language: string;
}

export default class Settings extends BaseController {
  public onInit(): void {
    // bind the view to the /Settings singleton; the VAT table uses an
    // absolute path of its own
    this.getView()!.setBindingContext(
      this.odata().createBindingContext("/Settings"),
      "odata",
    );
    this.setState<SettingsView>("view", { newVatRate: "" });
    this.setState<SettingsDraft>("settingsDraft", {
      stampWord: "",
      endingText: "",
      language: "",
    });
    void this.loadSettings();
  }

  /**
   * Fills the form. The read goes through the OData model like every other
   * read (D-002); only the write in onSave goes out through fetch.
   */
  private async loadSettings(): Promise<void> {
    try {
      const context = this.odata().bindContext("/Settings").getBoundContext();
      const settings = (await context.requestObject()) as SettingsRow;
      this.state<SettingsDraft>("settingsDraft").setData({
        stampWord: settings.StampWord ?? "",
        endingText: settings.EndingText ?? "",
        language: settings.Language ?? "",
      });
    } catch (error) {
      api.showError(this, error);
    }
  }

  public async onSave(): Promise<void> {
    const draft = this.state<SettingsDraft>("settingsDraft").getData();
    try {
      await api.patchSettings({
        StampWord: draft.stampWord,
        EndingText: draft.endingText,
        Language: draft.language,
      });
      MessageBox.information(this.i18nText("saved"));
    } catch (error) {
      api.showError(this, error);
    }
  }

  public async onAddVatRate(): Promise<void> {
    const view = this.state<SettingsView>("view");
    const rate = Number(view.getProperty("/newVatRate"));
    if (!rate || rate <= 0) {
      api.showError(this, new Error("InvalidVatRate"));
      return;
    }

    try {
      await api.createVatRate(rate);
      view.setProperty("/newVatRate", "");
      this.odata().refresh();
    } catch (error) {
      api.showError(this, error);
    }
  }

  public async onDeleteVatRate(event: Button$PressEvent): Promise<void> {
    const context = event.getSource().getBindingContext("odata") as Context;
    const id = context.getProperty("Id") as number;
    try {
      await api.deleteVatRate(id);
      this.odata().refresh();
    } catch (error) {
      api.showError(this, error);
    }
  }
}
