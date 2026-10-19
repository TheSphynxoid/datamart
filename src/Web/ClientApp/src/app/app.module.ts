import { APP_ID, NgModule, inject, provideAppInitializer } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { LucideAngularModule, Sun, Moon, Laptop, Plus, Settings, MoreHorizontal, Menu, Home, Upload, CheckCircle, AlertTriangle, AlertCircle, Info, X, Eye, EyeOff, Search, ChevronDown, ChevronRight, CornerDownRight } from 'lucide-angular';
import { HTTP_INTERCEPTORS, provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';

import { AppComponent } from './app.component';
import { AppShellComponent } from './app-shell/app-shell.component';
import { DashboardComponent } from './Dashboard/dashboard.component';
import { ThemeToggleComponent } from './theme-toggle/theme-toggle.component';
import { API_BASE_URL } from './web-api-client';
import { AuthorizeInterceptor } from 'src/api-authorization/authorize.interceptor';
import { XsrfInterceptor } from 'src/api-authorization/xsrf.interceptor';
import { LoginComponent } from 'src/api-authorization/login/login.component';
import { RegisterComponent } from 'src/api-authorization/register/register.component';
import { AuthGuard } from 'src/api-authorization/auth.guard';
import { AuthService } from 'src/api-authorization/auth.service';

import { FileUploadComponent } from './file-upload/file-upload.component';
import { ExcelMapComponent } from './Excelmap/excelmap.component';
import { BatchJournalComponent } from './BatchJournal/batch-journal.component';
import { ReportErrorsComponent } from './report-errors/report-errors.component';
import { EntityRulesComponent } from './entity-rules/entity-rules.component';
import { RuleListComponent } from './entity-rules/rule-list.component';
import { DxDataGridModule } from 'devextreme-angular/ui/data-grid';
import { DxFormModule } from 'devextreme-angular/ui/form';
import { DxButtonModule } from 'devextreme-angular/ui/button';
import { DxCheckBoxModule } from 'devextreme-angular/ui/check-box';
import { DxSelectBoxModule } from 'devextreme-angular/ui/select-box';
import { DxStepperModule } from 'devextreme-angular/ui/stepper';
import { DxTextBoxModule } from 'devextreme-angular/ui/text-box';
import { DxDateBoxModule } from 'devextreme-angular/ui/date-box';
import { DxDropDownBoxModule } from 'devextreme-angular/ui/drop-down-box';
import { DxLoadIndicatorModule } from 'devextreme-angular/ui/load-indicator';
import { DxProgressBarModule } from 'devextreme-angular/ui/progress-bar';
import { DxValidatorModule } from 'devextreme-angular/ui/validator';
import { DxValidationSummaryModule } from 'devextreme-angular/ui/validation-summary';
import { DxToolbarModule } from 'devextreme-angular/ui/toolbar';
import { DxDropDownButtonModule } from 'devextreme-angular/ui/drop-down-button';

export function getApiBaseUrl(): string {
  const url = document.getElementsByTagName('base')[0].href;
  return url.endsWith('/') ? url.slice(0, -1) : url;
}

@NgModule({
    declarations: [
        AppComponent,
        AppShellComponent,
        DashboardComponent,
        ThemeToggleComponent,
        LoginComponent,
        RegisterComponent,
        FileUploadComponent,
        ExcelMapComponent,
        BatchJournalComponent,
        ReportErrorsComponent,
        EntityRulesComponent,
        RuleListComponent
    ],
    bootstrap: [AppComponent],
    imports: [
        BrowserModule,
        FormsModule,
        DxDataGridModule,
        DxFormModule,
        DxButtonModule,
        DxCheckBoxModule,
        DxSelectBoxModule,
        DxStepperModule,
        DxTextBoxModule,
        DxLoadIndicatorModule,
        DxProgressBarModule,
        DxValidatorModule,
        DxValidationSummaryModule,
        DxToolbarModule,
        DxDropDownButtonModule,
        DxDateBoxModule,
        DxDropDownBoxModule,
        LucideAngularModule.pick({ Sun, Moon, Laptop, Plus, Settings, MoreHorizontal, Menu, Home, Upload, CheckCircle, AlertTriangle, AlertCircle, Info, X, Eye, EyeOff, Search, ChevronDown, ChevronRight, CornerDownRight }),
        RouterModule.forRoot([
            { path: '', component: DashboardComponent, pathMatch: 'full', canActivate: [AuthGuard] },
            { path: 'login', component: LoginComponent },
            { path: 'register', component: RegisterComponent },
            { path: 'excelmap', component: ExcelMapComponent, canActivate: [AuthGuard] },
            { path: 'journal/:id', component: BatchJournalComponent, canActivate: [AuthGuard] },
            { path: 'rules', component: EntityRulesComponent, canActivate: [AuthGuard] }
        ])
    ],
    providers: [
        { provide: APP_ID, useValue: 'ng-cli-universal' },
        { provide: HTTP_INTERCEPTORS, useClass: AuthorizeInterceptor, multi: true },
        { provide: HTTP_INTERCEPTORS, useClass: XsrfInterceptor, multi: true },
        { provide: API_BASE_URL, useFactory: getApiBaseUrl, deps: [] },
        provideAppInitializer(() => inject(AuthService).initialize()),
        provideHttpClient(withInterceptorsFromDi())
    ]
})
export class AppModule { }
