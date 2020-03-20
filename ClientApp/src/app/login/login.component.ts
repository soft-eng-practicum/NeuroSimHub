import { Component, OnInit } from '@angular/core';
import { FormGroup, FormControl, Validators, FormBuilder} from '@angular/forms';
import { AccountService } from '../services/account.service';
import { Router, ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css']
})
export class LoginComponent implements OnInit {

  insertForm: FormGroup;
  Username: FormControl;
  Password: FormControl;
  returnUrl: string;
  ErrorMessage: string;
  ErrorMessagePopup: boolean;
  invalidLogin: boolean;

  constructor(private acct : AccountService, 
              private router : Router,
              private route : ActivatedRoute,
              private formBuilder : FormBuilder
              ) { }

  ngOnInit() {
    // Initialize Form Controls
    this.Username = new FormControl('', [Validators.required]);
    this.Password = new FormControl('', [Validators.required]);

    this.ErrorMessagePopup = false;

    // get return url from route parameters or default to '/'
    this.returnUrl = this.route.snapshot.queryParams['returnUrl'] || '/';

     // Initialize FormGroup using FormBuilder
    this.insertForm = this.formBuilder.group({
        "Username" : this.Username,
        "Password" : this.Password
    
    });

  }

  onSubmit()
  {
    let userLogin = this.insertForm.value;

    this.acct.login(userLogin.Username, userLogin.Password).subscribe(
      result => {
        let token = (<any>result).token;      
        this.invalidLogin = false;
        this.router.navigate(['/home']);
        console.log('Log in Successfully');
      },
      error => {
        this.invalidLogin = true;
        this.ErrorMessage = "Invalid details Supplied - Could not Log in";
        this.ErrorMessagePopup = true;
        console.log(error);
      }
    );

  }

}
