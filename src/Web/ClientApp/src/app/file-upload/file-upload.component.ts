import { Component, output } from '@angular/core';

@Component({
    standalone: false,
    selector: 'app-file-upload',
    templateUrl: './file-upload.component.html'
})

export class FileUploadComponent {
    errorMessage = '';
    successMessage = '';
    SelectedFile = output<File>();

    async onFileSelected(event: any) {
        const file = event.target.files[0];
        this.errorMessage = '';
        this.successMessage = '';

        if (!file) return;

        // Validate file type and size
        if (!this.isValidFileType(file)) {
            this.errorMessage = 'Invalid file type. Please upload an .xlsx workbook.';
            return;
        }

        if (file.size > 100 * 1024 * 1024) { // 100 MB limit
            this.errorMessage = 'File size exceeds 100 MB.';
            return;
        }

        // Validate file content (advanced check)
        const isValid = await this.validateFileContent(file);

        if (isValid) {
            this.SelectedFile.emit(file);
        }
    }

    isValidFileType(file: File): boolean {
        // Excel (.xlsx) only — CSV is not supported; the server only parses OOXML packages.
        const allowedTypes = ['application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'];
        return allowedTypes.includes(file.type);
    }

    async validateFileContent(file: File) : Promise<boolean> {
        const buffer = await file.arrayBuffer();
        const bytes = new Uint8Array(buffer.slice(0, 4));

        // Basic signature (magic number) check — the full OOXML / zip-bomb / macro
        // validation is enforced on the server (XlsxPackageInspector).
        const xlsxHeader = [0x50, 0x4B, 0x03, 0x04];

        if (this.startsWith(bytes, xlsxHeader)) {
            this.successMessage = 'Valid XLSX file uploaded!';
            return true;
        } else {
            this.errorMessage = 'Invalid file content. Please upload a valid .xlsx workbook.';
        }
        return false;
    }

    private startsWith(bytes: Uint8Array, header: number[]): boolean {
        return header.every((b, i) => bytes[i] === b);
    }
}