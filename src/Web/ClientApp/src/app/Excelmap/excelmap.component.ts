import { Component } from '@angular/core';

@Component({
  standalone: false,
  selector: 'app-excelmap',
  templateUrl: './excelmap.component.html'
})
export class ExcelMapComponent {

  selectedFile?: File;

  onFileSelected(file: File) {
    this.selectedFile = file;
  }

}
