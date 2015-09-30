using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;


namespace WpfAsyncValidation
{
    public abstract class AsyncValidationViewModelBase: INotifyPropertyChanged, INotifyDataErrorInfo, IValidationExceptionHandler
    {

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChangedEvent(string propertyName)
        {
            if(PropertyChanged != null)
            {
                this.PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }

        }



        protected void RaisePropertyChangedEvent(Expression<Func<object>> exp)
        {
            string propertyName = ObjectHelper.GetName(exp);
            this.RaisePropertyChangedEvent(propertyName);
        }

        #endregion


        public abstract void ShowErrorDialog(Exception ex);
  

        public AsyncValidationViewModelBase()
        {
           

          
        }


        #region validation
     
        public void SubscribePropertyChanged(Action action, params string[] propertyNames)
        {
            if(action == null || !propertyNames.AnyEx())
                return;

            this.PropertyChanged += (sender, args) =>
            {
                if(action != null && propertyNames.Contains(args.PropertyName))
                {
                    action();
                }
             
            };
        }

        public void SubscribePropertyChanged(string propertyName, Action action)
        {
            if (action == null || propertyName==null)
                return;

            this.PropertyChanged += (sender, args) =>
            {
                if (action != null && args.PropertyName == propertyName)
                {
                    action();
                }
            };
        }

        public void SubscribeHasErrorsChanged(Action action)
        {
            this.SubscribePropertyChanged("HasErrors", action);
        }

        public void SubscribeIsValidatingChanged(Action action)
        {
            this.SubscribePropertyChanged("IsValidating", action);
        }


        public bool EnableShowErrorInDialog { get; set; }
        private bool _validateAllProperties = true;
        public bool ValidateAllProperties
        {
            get { return _validateAllProperties; }
            set
            {
                _validateAllProperties = value;
            }
        }

        private ConcurrentDictionary<string, IList<ValidationResult>> modelErrors = new ConcurrentDictionary<string, IList<ValidationResult>>();
        protected void NotifyErrorsChanged(String propertyName)
        {
            if (ErrorsChanged != null)
                ErrorsChanged(this, new DataErrorsChangedEventArgs(propertyName));
        }


        protected void NotifyErrorsChanged(Expression<Func<object>> exp)
        {
            var name = ObjectHelper.GetName(exp);
            NotifyErrorsChanged(name);
        }

        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        public virtual System.Collections.IEnumerable GetErrors(string propertyName)
        {
            if(propertyName == null)
            {
                propertyName = string.Empty;
            }
            IList<ValidationResult> propertyErrors = null;
            modelErrors.TryGetValue(propertyName, out propertyErrors);
            return propertyErrors;
        }

        public virtual bool HasErrors
        {
            get { return modelErrors.Count > 0; }
        }

        public bool IsPropertyValid(string propertyName)
        {
            IList<ValidationResult> existingResult;
            if (modelErrors.TryGetValue(propertyName, out existingResult))
            {
                return !existingResult.AnyEx();
            }

            return true;
        }


        public void ValidationExceptionsChanged(string propertyName, int count, Exception ex)
        {
            if (propertyName == null)
                propertyName = string.Empty;

            ClearPropertyError(propertyName);
            if (count > 0)
            {
                AddPropertyError(propertyName, ex.Message);
            }

            RaisePropertyChangedEvent(() => HasErrors);
            NotifyErrorsChanged(propertyName);
            RaisePropertyChangedEvent(() => propertyName);
        }

        protected void ClearPropertyError(string propertyName)
        {
            IList<ValidationResult> propertyValidationResult;
            modelErrors.TryRemove(propertyName, out propertyValidationResult);
        }

        protected void ClearAllErrors()
        {
            foreach (var propName in modelErrors.Keys)
            {
                IList<ValidationResult> propertyValidationResult;
                modelErrors.TryRemove(propName, out propertyValidationResult);
            }
        }

        protected ValidationResult AddPropertyError(string propertyName, string error)
        {
            var result = new ValidationResult(error, propertyName.ToSingleListEx());
            AddPropertyError(propertyName, result);

            return result;
        }

        protected void AddPropertyError(string propertyName, ValidationResult result)
        {
            IList<ValidationResult> existingResult;
            if (modelErrors.TryGetValue(propertyName, out existingResult))
            {
                existingResult.Add(result);
            }
            else
            {
                modelErrors.TryAdd(propertyName, result.ToSingleListEx());
            }

        }

        protected void AddPropertyError(string propertyName, IEnumerable<ValidationResult> results)
        {
            if (!results.AnyEx())
                return;

            IList<ValidationResult> existingResult;
            if (modelErrors.TryGetValue(propertyName, out existingResult))
            {
                foreach (var res in results)
                {
                    existingResult.Add(res);
                }
            }
            else
            {
                modelErrors.TryAdd(propertyName, results.ToList());
            }

        }

        protected static string[] GetMemberNamesFromValidationResults(IEnumerable<ValidationResult> results)
        {
            if (!results.AnyEx())
                return new string[0];

            return results.Where(r => r != ValidationResult.Success).
                SelectMany(res => res.MemberNames)
                .Select(s => s == null ? string.Empty : s)
                .Distinct()
                .ToArray();

        }

        private int _isValidating = 0;
        public virtual bool IsValidating
        {
            get { return _isValidating > 0; }
        }

        public Task ValidateAsync(Action onCompletion = null)
        {
            return ValidateAsync(CancellationToken.None, onCompletion);
        }

        public async Task ValidateAsync(CancellationToken cancellationToken, Action onCompletion = null)
        {
         
            Interlocked.Increment(ref _isValidating);
            RaisePropertyChangedEvent(() => IsValidating);

            string objectName = "";
            IList<string> memberNames = new List<string>();

            try
            {

                IEnumerable<ValidationResult> results = await Task.Run(async () =>
                {

                    ClearAllErrors();


                    var validationResults = await ValidateCoreAsync(cancellationToken);

                    return validationResults;
                });

                var errorResults = results.Where(res => res != ValidationResult.Success).ToList();
                //distribute error to the first member name
                foreach (ValidationResult res in errorResults)
                {
                    string propName = res.MemberNames.FirstOrDefault(m => !string.IsNullOrEmpty(m));
                    if (propName != null)
                    {
                        AddPropertyError(propName, res);
                    }

                }

                var affectedProperties = GetMemberNamesFromValidationResults(results).ToList();
                affectedProperties.ForEach(m => memberNames.Add(m));

                if (!memberNames.Contains(objectName))
                {
                    memberNames.Add(objectName); //in case of success, it may return no member names
                }

          

            }
            catch (Exception ex)
            {
                if (EnableShowErrorInDialog)
                {
                    ShowErrorDialog(ex);
                }

                AddPropertyError(objectName, ex.Message);
                memberNames = objectName.ToSingleListEx();
            }
            finally
            {
                //raise event in UI thread
                RaisePropertyChangedEvent(() => HasErrors);

                foreach (string prop in memberNames)
                {
                    NotifyErrorsChanged(prop);
                    RaisePropertyChangedEvent(() => prop);
                }

                Interlocked.Decrement(ref _isValidating);
                RaisePropertyChangedEvent(() => IsValidating);

            }

            if (onCompletion != null)
            {
                onCompletion();
            }

        }


        public Task ValidatePropertyAsync(object propertyValue, Expression<Func<object>> expr, Action onCompletion = null)
        {
            return this.ValidatePropertyAsync(propertyValue, ObjectHelper.GetName(expr), onCompletion, CancellationToken.None);
        }

        public Task ValidatePropertyAsync(object propertyValue, string propertyName, Action onCompletion = null)
        {
            return this.ValidatePropertyAsync(propertyValue, propertyName, onCompletion, CancellationToken.None);
        }


        public async Task ValidatePropertyAsync(object propertyValue, string propertyName, Action onCompletion, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _isValidating);
            RaisePropertyChangedEvent(() => IsValidating);

            IList<string> memberNames = new List<string>();
            try
            {

                IEnumerable<ValidationResult> results = await Task.Run(async () =>
                {

                    bool isThreadPoolThread = Thread.CurrentThread.IsThreadPoolThread;

                    ClearPropertyError(propertyName);


                    var validationResults = await ValidatePropertyCoreAsync(propertyValue, propertyName, cancellationToken);

                    var errorResults = validationResults.Where(res => res != ValidationResult.Success).ToList();

                    if (errorResults.AnyEx())
                    {
                        AddPropertyError(propertyName, errorResults);
                    }

                    return validationResults;
                });

                var affectedProperties = GetMemberNamesFromValidationResults(results).ToList();
                affectedProperties.ForEach(m => memberNames.Add(m));

                if (!memberNames.Contains(propertyName))
                {
                    memberNames.Add(propertyName); //in case of success, it may return no member names
                }

              

            }
            catch (Exception ex)
            {
                if (EnableShowErrorInDialog)
                {
                    ShowErrorDialog(ex);
                }

                AddPropertyError(propertyName, ex.Message);
                memberNames = propertyName.ToSingleListEx();
            }
            finally
            {

                RaisePropertyChangedEvent(() => HasErrors);
                foreach (string prop in memberNames)
                {
                    NotifyErrorsChanged(prop);
                    RaisePropertyChangedEvent(() => prop);
                    //RaisePropertyChangedEvent(() => "Item[]");

                }

                //ensure IsValidating updated
                Interlocked.Decrement(ref _isValidating);
                RaisePropertyChangedEvent(() => IsValidating);

            }

            if (onCompletion != null)
            {
                onCompletion();
            }

        }



        protected virtual Task<IEnumerable<ValidationResult>> ValidateCoreAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var validationContext = new ValidationContext(this, null, null);
                ICollection<ValidationResult> validationResults = new List<ValidationResult>();
                Validator.TryValidateObject(this, validationContext, validationResults, ValidateAllProperties);

                //maybe muiltiple results returned
                return ((IEnumerable<ValidationResult>)validationResults);
            });
        }

        protected virtual Task<IEnumerable<ValidationResult>> ValidatePropertyCoreAsync(object propertyValue, string propertyName, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var validationContext = new ValidationContext(this, null, null);
                validationContext.MemberName = propertyName;
                ICollection<ValidationResult> validationResults = new List<ValidationResult>();
                Validator.TryValidateProperty(propertyValue, validationContext, validationResults);

                //maybe muiltiple results returned
                return ((IEnumerable<ValidationResult>)validationResults);
            });


        }

        


        #endregion



    }

    public interface IValidationExceptionHandler
    {
        void ValidationExceptionsChanged(string propertyName, int count, Exception ex);
    }
}

